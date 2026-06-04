# DeviceOrder

Make your **main device** (typically a sim racing wheel) become **device 1** without unplugging anything.

A tiny, free Windows tool for mixed setups (wheel + pedals + handbrake + button box, often from different brands) where a game refuses to bind your main device, or gives no force feedback, until it is recognized as "device 1". Instead of physically unplugging every other device, DeviceOrder does the same thing in software, in one click, fully reversible.

Developed and tested for Forza Horizon, but it works for any game that forces you to isolate one device.

## How it works

1. Mark your main device (one click). It is highlighted and protected, never disabled.
2. Set the order of your devices with Move up / Move down (saved automatically).
3. Click **Hide all except the Main**. The other devices are disabled one by one, so your main device is alone as device 1.
4. Launch the game and bind your main device.
5. Click the same button (now **Restore all**) and the others come back, in the same order.

Under the hood, it disables and re-enables the USB node of each controller through the official Windows PnP API (the same operation as the `Disable-PnpDevice` / `Enable-PnpDevice` cmdlets, which is the same as right click then Disable in Device Manager).

## What it does NOT do (for your peace of mind)

* No driver installed.
* No network access, no telemetry, no data collection.
* No DLL injection into the game, so no anti cheat risk.
* It never touches your keyboard or your mouse (excluded automatically).
* Everything is reversible. A Windows reboot also resets it all.

## Antivirus note

The released binary is new and not code signed, so Windows SmartScreen or some antivirus engines may show a **false positive**. The tool only enables and disables controllers through the Windows API, nothing else. You can read the full source here and build it yourself.

## Build from source

No SDK required. The build uses the `csc.exe` compiler that ships with the .NET Framework already installed on Windows:

```powershell
.\build.ps1
```

This produces a single `DeviceOrder.exe`. It needs administrator rights at runtime (to enable and disable devices), so the manifest requests elevation.

## Languages

English, French, German, Spanish, Italian and Portuguese. The language is asked on first launch and can be changed at any time.

## Files

* `Program.cs` : the whole application (WinForms, device detection, UI, safety nets).
* `build.ps1` : compiles the single exe.
* `app.manifest` : requests administrator elevation.
* `logo.svg`, `app.ico`, `DeviceOrder.ico` : assets.

## Trademarks

DeviceOrder is an independent tool and is not affiliated with, endorsed by, or sponsored by any game publisher or hardware brand. All game and brand names are used for compatibility reference only and belong to their respective owners.

## License

MIT, see [LICENSE](LICENSE).

## Support

If this saved you some cable pain, you can support the project: https://www.paypal.com/paypalme/SinepticStudio
