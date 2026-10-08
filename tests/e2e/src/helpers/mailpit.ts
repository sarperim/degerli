import type { APIRequestContext } from '@playwright/test'

import { MAILPIT_BASE_URL } from '../config'

/**
 * MailPit HTTP API helper (test strategy §5.4, §7, §15).
 *
 * Retrieves messages — and the links they carry — with **bounded polling**
 * (default ≤ 10 s) and an explicit timeout error; never a bare sleep. MailPit is
 * the SMTP double in CI, so this is how verification/reset links reach e2e tests.
 *
 * The `sendProbe` method is the probe flow used to prove the helper works: it
 * injects a message through MailPit's HTTP Send API, so the harness does not need
 * a product mail endpoint (none exists yet) nor an SMTP client dependency.
 */

export const MAILPIT_DEFAULT_TIMEOUT_MS = 10_000
export const MAILPIT_POLL_INTERVAL_MS = 250

export interface MailpitAddress {
  Name?: string
  Address: string
}

export interface MailpitMessageSummary {
  ID: string
  MessageID?: string
  Subject: string
  From: MailpitAddress
  To: MailpitAddress[]
  Created: string
  Snippet?: string
  Tags?: string[]
}

export interface MailpitMessageList {
  total: number
  unread: number
  messages_count?: number
  start: number
  messages: MailpitMessageSummary[]
}

export interface MailpitMessage {
  ID: string
  Subject: string
  From: MailpitAddress
  To: MailpitAddress[]
  Cc?: MailpitAddress[]
  Date: string
  Text: string
  HTML: string
}

export interface WaitOptions {
  /** Hard upper bound on the wait. Defaults to {@link MAILPIT_DEFAULT_TIMEOUT_MS}. */
  timeoutMs?: number
  /** Delay between polls. Defaults to {@link MAILPIT_POLL_INTERVAL_MS}. */
  intervalMs?: number
  /** Human-readable description used in the timeout error. */
  description?: string
}

/** Thrown when a bounded MailPit wait elapses without a match (explicit, never silent). */
export class MailpitTimeoutError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'MailpitTimeoutError'
  }
}

export interface ProbeMessageInput {
  from: string
  to: string
  subject: string
  text: string
}

const URL_PATTERN = /https?:\/\/[^\s"'<>()]+/g

/** All URLs found in a message's text and HTML bodies. */
export function extractLinks(message: Pick<MailpitMessage, 'Text' | 'HTML'>): string[] {
  const found = new Set<string>()
  for (const source of [message.Text ?? '', message.HTML ?? '']) {
    for (const match of source.matchAll(URL_PATTERN)) {
      found.add(match[0])
    }
  }
  return [...found]
}

export class MailpitClient {
  constructor(
    private readonly request: APIRequestContext,
    private readonly baseUrl: string = MAILPIT_BASE_URL,
    private readonly defaultTimeoutMs: number = MAILPIT_DEFAULT_TIMEOUT_MS,
  ) {}

  private url(path: string): string {
    return `${this.baseUrl}${path}`
  }

  /** Empties the mailbox so a test only sees messages it caused. */
  async clear(): Promise<void> {
    const response = await this.request.delete(this.url('/api/v1/messages'))
    if (!response.ok()) {
      throw new Error(`MailPit: clear failed with status ${response.status()}`)
    }
  }

  /** Newest-first list of stored messages. */
  async list(): Promise<MailpitMessageList> {
    const response = await this.request.get(this.url('/api/v1/messages?limit=200'))
    if (!response.ok()) {
      throw new Error(`MailPit: list failed with status ${response.status()}`)
    }
    return (await response.json()) as MailpitMessageList
  }

  /** Full message body (text + HTML) by id. */
  async get(id: string): Promise<MailpitMessage> {
    const response = await this.request.get(this.url(`/api/v1/message/${id}`))
    if (!response.ok()) {
      throw new Error(`MailPit: get(${id}) failed with status ${response.status()}`)
    }
    return (await response.json()) as MailpitMessage
  }

  /** Probe flow: inject a message through MailPit's HTTP Send API; returns its id. */
  async sendProbe(input: ProbeMessageInput): Promise<string> {
    const response = await this.request.post(this.url('/api/v1/send'), {
      data: {
        From: { Email: input.from },
        To: [{ Email: input.to }],
        Subject: input.subject,
        Text: input.text,
      },
    })
    if (!response.ok()) {
      throw new Error(`MailPit: send failed with status ${response.status()}`)
    }
    const body = (await response.json()) as { ID: string }
    return body.ID
  }

  /**
   * Polls until a message summary satisfies `match`, or throws
   * {@link MailpitTimeoutError} once `timeoutMs` elapses.
   */
  async waitForMessage(
    match: (message: MailpitMessageSummary) => boolean,
    options: WaitOptions = {},
  ): Promise<MailpitMessageSummary> {
    const timeoutMs = options.timeoutMs ?? this.defaultTimeoutMs
    const intervalMs = options.intervalMs ?? MAILPIT_POLL_INTERVAL_MS
    const description = options.description ?? 'the supplied predicate'
    const deadline = Date.now() + timeoutMs

    let received = 0
    for (;;) {
      const list = await this.list()
      received = list.messages.length
      const found = list.messages.find(match)
      if (found) return found

      if (Date.now() >= deadline) {
        throw new MailpitTimeoutError(
          `MailPit: no message matching ${description} within ${timeoutMs} ms ` +
            `(${received} message(s) received).`,
        )
      }
      await delay(intervalMs)
    }
  }

  /**
   * Polls until a message carries a link matching `pattern`, returning the summary
   * and the first matching link, or throws {@link MailpitTimeoutError}.
   */
  async waitForLink(
    pattern: RegExp,
    options: WaitOptions = {},
  ): Promise<{ message: MailpitMessageSummary; link: string }> {
    const timeoutMs = options.timeoutMs ?? this.defaultTimeoutMs
    const intervalMs = options.intervalMs ?? MAILPIT_POLL_INTERVAL_MS
    const description = options.description ?? `link ${pattern}`
    const deadline = Date.now() + timeoutMs

    let received = 0
    for (;;) {
      const list = await this.list()
      received = list.messages.length
      for (const summary of list.messages) {
        const full = await this.get(summary.ID)
        const link = extractLinks(full).find((candidate) => pattern.test(candidate))
        if (link) return { message: summary, link }
      }

      if (Date.now() >= deadline) {
        throw new MailpitTimeoutError(
          `MailPit: no message carrying ${description} within ${timeoutMs} ms ` +
            `(${received} message(s) received).`,
        )
      }
      await delay(intervalMs)
    }
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}
