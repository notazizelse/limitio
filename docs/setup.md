# Installing LimitIO

This walks through installing LimitIO on a Windows 10/11 PC (your own, or one you're setting up for someone else), with no prior experience assumed.

## 1. Download the installer

1. Go to this repository's **Releases** page (on GitHub, click "Releases" in the right-hand sidebar, or go to `https://github.com/<owner>/<repo>/releases`).
2. Under the latest release, click on `LimitIO-Setup-<version>.exe` to download it.

## 2. Run it

1. Double-click the downloaded `.exe`.
2. Windows will very likely show a blue **"Windows protected your PC"** SmartScreen warning. This is expected and normal for a small open-source tool that hasn't paid for a code-signing certificate - it does not mean the file is unsafe, only that Microsoft doesn't yet recognize the publisher. Click **"More info"**, then **"Run anyway"**.
3. You'll get a **User Account Control (UAC)** prompt asking to let the installer make changes to your device. Click **Yes** - this is required because LimitIO installs a background Windows Service, which needs administrator rights to register.
4. Click through the installer (accept the license, keep the default install location unless you have a reason not to).

## 3. First-run setup

Right after installing, LimitIO opens automatically and asks you to **set a management password**. This is separate from your Windows login password - it's what you (or whoever manages the limits) will type in to change rules, pause monitoring, or change the password itself later. Anyone using the computer can still see today's usage and use the once-a-day one-minute "ignore" grace without this password - only *changing* things needs it.

Pick a password only you know, and don't reuse a password that matters elsewhere in case this device is later shared.

## 4. Add your first limit

1. Click the LimitIO icon in the system tray (bottom-right, near the clock) and choose **Open LimitIO...**.
2. Enter your management password.
3. Click **Add...**, choose whether you're limiting a website (by domain, e.g. `instagram.com`) or an application (by process name, e.g. `telegram`), set a daily minutes budget, and save.

## 5. Installing on another PC

LimitIO doesn't need to be built from source to install elsewhere - just repeat steps 1-4 on that PC using the same installer `.exe` (or download it fresh from the Releases page there). There's nothing to configure differently per machine.

If you want the stronger, iPhone-Screen-Time-like guarantee where the person being limited genuinely cannot turn LimitIO off, set them up with a **standard (non-administrator) Windows account** rather than sharing your own administrator account, and keep the admin account's password and the LimitIO management password to yourself. See [ARCHITECTURE.md](ARCHITECTURE.md) for exactly why this matters.

## Uninstalling

Use Windows' normal **"Add or remove programs"** and remove LimitIO like any other app. Your configured limits, usage history, and password are kept on disk (under `%ProgramData%\LimitIO`) in case you're about to reinstall an update - if you want to fully wipe that too, delete that folder afterward (you'll need to be an administrator to do this, by design).

## Troubleshooting

- **"Couldn't reach the LimitIO service"** when opening the tray app: the background service isn't running. Open Services (`services.msc`), find **LimitIO Enforcement Service**, and check its status. If it's stopped and won't start, check `%ProgramData%\LimitIO\logs\` for the service's log file, and `%ProgramData%\LimitIO\install.log` for the installer's own log.
- **You can't stop the service from Services.msc / Task Manager as a standard user**: this is intentional, not a bug - see [ARCHITECTURE.md](ARCHITECTURE.md).
