import { describe, expect, it } from 'vitest'
import { screen, within } from '@testing-library/react'

import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { runA11y } from '@/test/a11y'
import { renderWithProviders } from '@/test/render'

function SampleTable() {
  return (
    <TableContainer>
      <Table>
        <TableCaption>Valuation metrics</TableCaption>
        <TableHeader>
          <TableRow>
            <TableHead>Metric</TableHead>
            <TableHead>Value</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow>
            <TableHead scope="row">P/E</TableHead>
            <TableCell>12.4</TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </TableContainer>
  )
}

/**
 * Semantic tables (architecture M-8; UXR-G-028). Native table semantics with a
 * caption and scoped headers so cells are announced with their headers.
 */
describe('Table primitives', () => {
  it('exposes a captioned table with column and row headers', () => {
    renderWithProviders(<SampleTable />)

    const table = screen.getByRole('table', { name: 'Valuation metrics' })
    expect(table).toBeInTheDocument()

    const columnHeaders = screen.getAllByRole('columnheader')
    expect(columnHeaders.map((cell) => cell.textContent)).toEqual([
      'Metric',
      'Value',
    ])
    expect(screen.getByRole('rowheader')).toHaveTextContent('P/E')

    const row = screen.getByRole('row', { name: /P\/E/ })
    expect(within(row).getByRole('cell')).toHaveTextContent('12.4')
  })

  it('has no detectable accessibility violations', async () => {
    const { container } = renderWithProviders(<SampleTable />)
    await expect(runA11y(container)).resolves.toEqual([])
  })
})
