# Windows 1.0.1 validation checklist

Use this checklist on a legitimate, native Windows installation. Proton results are
useful development evidence but do not validate Windows itself.

## Prepare

1. In Steam, verify Crawl's installed files and launch it once without the mod.
2. Confirm the title screen reports `v1.0.1`, then close Crawl.
3. Extract the Crawl Online release into a new directory. Do not copy game or Steam
   DLLs into that directory.
4. Open PowerShell there and run:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\install-release-windows.ps1 install -Package .
   .\install-release-windows.ps1 diagnose
   ```

## Reproduce

1. Start Crawl with the normal Steam **Play** button.
2. Record whether `ONLINE` appears between `START GAME` and `THE VAULT`.
3. Open `ONLINE`, host, open `INVITE FRIENDS`, close the overlay, then cancel.
4. Close Crawl normally. If it hangs, record the last visible screen and terminate
   only the Crawl process; do not delete files before collecting diagnostics.
5. Write down the exact action, expected result, observed result, and whether the
   problem happens every time.

## Collect safe evidence

Run:

```powershell
.\collect-diagnostics-windows.ps1
```

For a non-standard Steam library, pass the same `-GameDir` used for installation.
The generated ZIP contains only a generated report, privacy notice, and the last
600 lines of the BepInEx log after sanitization. It excludes executables, DLLs,
saves, raw configuration, credentials, absolute user paths, long Steam/lobby IDs,
and network addresses. Review every text file before sharing the archive.

When reporting a problem, attach the ZIP and include:

- native Windows version and whether Windows is 64-bit;
- controller model, if relevant;
- exact reproduction steps;
- screenshot of the last visible state, if safe;
- whether Steam overlay is enabled.

Never attach Crawl binaries, `Assembly-CSharp.dll`, `steam_api.dll`, Steam library
contents, saves, or decompiled source.

## Restore

To remove only Crawl Online while preserving BepInEx and unrelated plugins:

```powershell
.\install-release-windows.ps1 uninstall
```

Use Steam's file verification only if you intentionally want Steam to restore the
base game. Keep the diagnostic ZIP until the issue has been reviewed.
