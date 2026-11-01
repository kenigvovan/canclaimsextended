# Prebuilt databases for the scenarios

| Folder | Made from | Used by | Case |
|---|---|---|---|
| `fixtures/FullWorld/canclaimsextended.db` | `fixtures/sql/FullWorld.sql` | `PersistenceScenarios` | Y1 |
| `fixtures/BrokenRows/canclaimsextended.db` | `fixtures/sql/BrokenRows.sql` | `DbLoadScenarios` | Y2 |

The `.db` files are not written by hand. `FixtureGenerator` (Category `FixtureGen`) runs each
SQL script into a fresh SQLite file. It runs inside a booted server, so it uses the game's own
SQLite and compares every table and column with the database the running mod has just created.
If the SQL no longer matches the mod's schema, it fails and names each difference.

## Generate (once, and again after a schema or SQL change)

```
set CX_GENERATE_FIXTURES=1
dotnet test canclaimsextended.scenarios --filter "Category=FixtureGen"
set CX_GENERATE_FIXTURES=
```

(PowerShell: `$env:CX_GENERATE_FIXTURES = "1"` ... `Remove-Item Env:CX_GENERATE_FIXTURES`.)

It writes each file twice:
- into this source tree, `fixtures/<Folder>/canclaimsextended.db`. Commit these files.
- next to the test assembly, `bin/.../fixtures/<Folder>/`, so the next run finds them without a rebuild.

Without the variable, the generator scenario only waits one tick and passes, so an ordinary run
never rewrites the fixtures.

## Before they exist

A class with `[AtlasDataFiles("fixtures/<Folder>", TargetPath = "ModConfig")]` still boots
without the `.db`. The folder exists because of these README files, and the mod starts on an empty
database. Every scenario of the class then fails first with
`fixture fixtures/<Folder>/canclaimsextended.db does not exist ...` (`Infrastructure/Fixtures.RequireDb`)
and tells you to run the commands above. It does not fail later with a puzzling "city was not loaded".

This README is copied into the server's `ModConfig` along with the database. The mod does not read it.

## Changing a fixture

The expected values are written out in `FixtureGenerator.cs` (`FullWorld`, `BrokenRows`). If you
change a value in the SQL, change it there too, then generate again. If the mod's schema changes
(a column in `SQLiteTables`, an `EnsureColumn`, a new table), the generator tells you what to add
to the `CREATE TABLE` lines of the scripts.
