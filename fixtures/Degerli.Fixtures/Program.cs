using Degerli.Fixtures;

// L4 (compose.ci) seeding entrypoint. See FixtureSeedEntry for the pass it runs.
// Exit code 0 = database migrated and seeded with the now-anchored fixture universe.
return await FixtureSeedEntry.RunAsync(args, Console.Out);
