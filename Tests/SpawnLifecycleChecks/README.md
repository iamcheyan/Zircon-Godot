# Spawn lifecycle regression checks

From the repository root, with the .NET 10 SDK:

```sh
dotnet run -c Release --project Tests/SpawnLifecycleChecks/SpawnLifecycleChecks.csproj
```

The executable references the actual `ServerLibrary` project. It loads generated map fixtures and builds definitions in an isolated temporary MirDB session. It does not start a server, bind sockets, or use any deployed database or map files.

The checks cover failed region placement and count accounting, successful placement and overlap, refilling a slot after live despawn, death-only `MONSTERCLEAR` behavior, corpse removal without a second decrement, and timed expiry of both special gates.
