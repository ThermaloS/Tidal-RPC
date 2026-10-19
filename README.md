# TIDAL Discord Rich Presence

Show what you're playing on TIDAL in your Discord profile, with album artwork and a link to the song.

## Example Screenshots

**Client**


<img width="632" height="722" alt="Screenshot 2026-09-13 184053" src="https://github.com/user-attachments/assets/ed2a16f2-a67a-4832-ab22-fb7c3b6fbcae" />


## Getting started

1. Download the .EXE
2. Run both Discord and Tidal on the same computer.
3. Play a song from Tidal
4. **Profit?**

Requires Windows 10 (version 2004 or later) or Windows 11, on an x64 PC. Works with the TIDAL desktop app, not browser, phone, or TIDAL Connect playback.

Double-click the tray icon to open Settings. You can change the appearance, choose what shows on Discord, and set the app to start with Windows. Closing Settings leaves it running in the tray.

Pause for five seconds and your Discord activity clears. Turn off **Share on Discord** whenever you want to stop sharing.

The app checks for updates on launch and daily. When prompted, open the download page, download the new EXE, exit TIDAL RPC, and replace your old copy. You can also choose **Check for updates** in Settings.

## macOS

The macOS version is a small background app with no window. It uses the same metadata service, Discord app and settings as the Windows version. It reads TIDAL through macOS Now Playing, runs at background priority and uses almost no CPU while idle.

Works with the TIDAL desktop app on Apple silicon and Intel Macs.

1. Build it on a Mac with the [.NET 10 SDK](https://dotnet.microsoft.com/download): `dotnet publish src/TidalRpc.Mac -c Release -o dist/macos`
2. Move `dist/macos/tidal-rpc` somewhere permanent, such as `~/Applications`.
3. Run `~/Applications/tidal-rpc install`. It starts now and at every login.

Change settings from Terminal. The running app picks up changes within a few seconds.

```text
tidal-rpc settings              Show settings
tidal-rpc set sharing off       Stop sharing (on to resume)
tidal-rpc set country GB        Country for song links
tidal-rpc set artwork off       Hide album artwork
tidal-rpc set timer off         Hide the song timer
tidal-rpc set button off        Hide the Play on TIDAL button
tidal-rpc set status song       Status text: artist, song or app
tidal-rpc uninstall             Stop and remove from login
```

To see what it's doing, run `tidal-rpc uninstall`, then `tidal-rpc run` in Terminal.

## Having trouble?

Make sure Discord's activity sharing is enabled. If something still isn't working, choose **Report a problem** in Settings or the tray menu. You can review the report before submitting it on GitHub.

## Privacy

While sharing is on, the app sends the song title, artist, album, duration, and your selected country to our metadata service to find artwork and a song link. Lookups are cached temporarily; the host may keep connection logs.

Bug reports include basic app diagnostics. Your current song is optional, and reports are never sent automatically.

---

Not affiliated with TIDAL or Discord.
