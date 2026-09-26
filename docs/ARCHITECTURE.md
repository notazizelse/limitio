# Architecture

## Solution layout

```
src/
  LimitIO.Core/              shared models, config/usage storage, password hashing, IPC contracts
  LimitIO.Service/           the privileged Windows Service: packet capture, enforcement, IPC server
  LimitIO.UI/                WPF tray app + password-gated settings UI
  LimitIO.Installer.Helper/  console tool the installer runs to apply service/ACL hardening
installer/
  LimitIO.iss                Inno Setup script -> single Setup.exe
tests/
  LimitIO.Core.Tests/
  LimitIO.Service.Tests/
```

`LimitIO.Core` has no Windows-specific dependencies and builds/tests on any platform. `LimitIO.Service`, `LimitIO.UI`, and `LimitIO.Installer.Helper` all target `net8.0-windows` and use Windows-only APIs (P/Invoke into `iphlpapi.dll`, `System.ServiceProcess`, WPF/WinForms) - they compile from source on Linux too (the .NET SDK ships Windows reference assemblies for cross-compilation), but only actually *run* on Windows.

## Why this exists / the honest security model

iPhone Screen Time's real security boundary is two things together: a privileged OS-level component the device owner can't just kill like a normal app, and a separate Screen Time passcode gating changes to it. It is not literally unbypassable - Apple has had to patch loopholes over time (e.g. a Face ID bypass path closed in iOS 26.4), and a full device erase always resets it. The goal was never "impossible to bypass," it was "hard enough to stop a kid poking around," and that's the bar this project targets too.

On Windows, the closest equivalent to "privileged OS component" is a **Windows Service running as SYSTEM**, hardened so a standard (non-administrator) account cannot stop, reconfigure, or delete it through normal tools (`sc`, Services.msc, Task Manager). LimitIO does exactly that - see `LimitIO.Installer.Helper` for the concrete `sc sdset` ACL, crash-recovery, and Scheduled-Task-backstop steps.

**What this genuinely stops:** a standard account cannot `sc stop`/`sc config`/`sc delete` the service, cannot delete or edit `%ProgramData%\LimitIO` (ACL'd to SYSTEM/Administrators only), and has no way to self-authorize past the management password - the UI never decides it's "unlocked," it must present a session token the Service itself issued after checking the password.

**What this does not and cannot stop:** an account with administrator rights can always reconfigure ACLs, take ownership of the service, boot into Safe Mode, or uninstall LimitIO entirely - Windows' own ownership rules guarantee this, the same way Apple's own "erase and set up as new" always resets Screen Time. If you need the stronger guarantee, the fix is the same one Screen Time relies on: put the restricted person on an account that isn't an administrator, and keep the admin account (and the LimitIO password) to yourself.

## Data flow

```
WinDivert packet -> ProcessConnectionTracker (GetExtendedTcpTable/UdpTable, IPv4 + IPv6) -> owning PID -> process name
                  -> (TCP:443/80 only) TlsClientHelloParser (SNI) / plain HTTP Host header -> hostname or null
                  -> FlowObservation{processName, domain?, flowKey, timestamp}
                  -> UsageAccountant: match enabled LimitRules (domain suffix match, or process name match)
                       -> accrue elapsed time per rule per day (interval-based, not per-byte) -> SQLite UsageStore
                  -> LimitEnforcer: Consumed >= DailyBudget && no active grace? -> don't reinject the packet
                  -> UI polls GetStatus over a named pipe -> renders usage bars / blocked state
                  -> "Ignore for 1 minute" -> RequestGrace (no password needed) -> allowed once/day/rule -> 60s reprieve
```

Not reinjecting a packet *is* the block - WinDivert hands a diverted packet to the service, and if the service doesn't send it back out, it simply never reaches the network. This is why a freshly-blocked app looks like it's hung or timed out rather than showing a clean "blocked" message: LimitIO has no way to inject its own UI into someone else's app, only to make its packets vanish.

## Key components

- **`Core/Models/LimitRule.cs`** - a domain or process-name rule with a daily budget; domain matching is by suffix (a rule for `instagram.com` also matches `www.instagram.com`).
- **`Core/Storage/ConfigStore.cs`** - atomic JSON persistence (rules + password hash/salt/iterations) via temp-file-then-replace, so a crash mid-write never corrupts it.
- **`Core/Storage/UsageStore.cs`** - SQLite-backed per-rule/per-day usage, chosen over a flat file because accounting is frequent small writes from a background timer happening concurrently with IPC reads.
- **`Core/Security/PasswordHasher.cs`** - PBKDF2-HMACSHA256, random salt, 310,000 iterations, constant-time comparison.
- **`Core/Ipc/Protocol.cs`** - the shared request/response DTOs used over the named pipe.
- **`Service/Capture/PacketCaptureEngine.cs`** - owns the WinDivert handle; filter is `outbound and (tcp.DstPort == 80 or tcp.DstPort == 443 or udp)`.
- **`Service/Capture/TlsClientHelloParser.cs`** - reads just the SNI extension from a ClientHello's plaintext header, without decrypting anything.
- **`Service/Attribution/ProcessConnectionTracker.cs`** - maps local address/port to owning PID via the IP Helper API, covering TCP and UDP, IPv4 and IPv6.
- **`Service/Accounting/UsageAccountant.cs`** - credits a fixed tick's worth of time to a rule once per tick if any matching flow was seen recently (so five tabs on the same limited site don't quintuple-count).
- **`Service/Enforcement/LimitEnforcer.cs` / `GraceManager.cs`** - the live block decision and the once-per-day 60-second grace.
- **`Service/Ipc/RequestRouter.cs` / `AuthSessionManager.cs`** - dispatches IPC requests; `GetStatus` and `RequestGrace` need no session token, everything that mutates state does.
- **`Installer.Helper/Commands/InstallCommand.cs`** - the concrete hardening steps (service creation, `sc sdset` ACL, `sc failure` recovery, Scheduled Task backstop, `icacls` directory lockdown).

## Known accuracy limitations

1. **Encrypted Client Hello (ECH).** A growing (still minority) slice of HTTPS traffic encrypts the SNI extension outright. When that happens, `TlsClientHelloParser` correctly returns "no hostname found" rather than guessing - domain-based rules simply won't match that connection. Process-name rules are unaffected, since they never depended on SNI.
2. **Domain-level, not full-URL, blocking.** LimitIO only ever sees a hostname, never a path - a rule for `youtube.com` cannot distinguish the homepage from a specific video, and sites that share a CDN hostname with unrelated content can occasionally be over- or under-blocked. Fixing this would require full TLS interception (a root certificate installed on the machine, breaking certificate pinning in other apps), which LimitIO deliberately does not do.
3. **Global rule set.** All rules currently apply machine-wide regardless of which Windows account is logged in. Per-account rule sets are a natural extension (matching a flow's owning session/user SID) but aren't implemented yet.
4. **A blocked connection looks like a stall, not an error.** See "Data flow" above - this is inherent to how packet-level blocking works, not a bug.
