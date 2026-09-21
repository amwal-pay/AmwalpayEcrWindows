# Compatibility

Which platforms `AmwalPay.Ecr` runs on, which of them we support, and exactly how
much of each claim was checked. Written for developers choosing whether and how to
adopt the package in a Windows native app (WinForms, WPF, console, Windows Service)
or a web app (ASP.NET Core, classic ASP.NET).

**The short answer.** Target **.NET Framework 4.6.2 or later** (**4.7.2 or later
recommended**), or **.NET 8 or later**, and use **C# 9 or later**. Those are the
supported minimums for both native and web apps. The package is plain managed code
with no native or Windows-only dependency, so the same package serves a till on
Windows and a service on any OS that runs .NET 8.

Two dates to plan around, both Microsoft's, not ours. **.NET 8 and .NET 9 leave
Microsoft support on Nov 10, 2026**, after which this SDK's supported minimum on
modern .NET becomes .NET 10. **.NET Framework 4.6.2 leaves Microsoft support on Jan
12, 2027**; after that the .NET Framework floor rises to 4.7.2 unless the owners
decide otherwise. See the [Support calendar](#support-calendar).

There is **no `net462` asset**. A 4.6.2 app gets the `netstandard2.0` build, which
brings `System.Text.Json` and its dependency graph with it
([listed below](#what-is-in-the-package)).

**Contents**

1. [Supported platforms](#supported-platforms)
2. [Support calendar](#support-calendar)
3. [What was verified, and what was not](#what-was-verified-and-what-was-not)
4. [What is in the package](#what-is-in-the-package)
5. [C# 9 or later](#c-9-or-later)
6. [.NET Framework](#net-framework)
7. [Windows native apps](#windows-native-apps)
8. [Web apps](#web-apps)
9. [Not part of the support statement](#not-part-of-the-support-statement)
10. [Sources](#sources)

---

## Supported platforms

Read the **Status** column first; the last column says what that status rests on.

| Platform | Asset used | Status | Verified how |
|---|---|---|---|
| .NET 10 | `net8.0` | **Supported.** Microsoft supports it until Nov 14, 2028 | Restored against the packed `.nupkg`, compiled, and **run**: a real sale against a signed fake terminal came back `Approved` |
| .NET 8, .NET 9 | `net8.0` | **Supported for now.** Microsoft ends support for both on **Nov 10, 2026**, after which the SDK's supported minimum becomes .NET 10 | .NET 8: restored, compiled, and **run** (the same real sale, `Approved`); it is the version the samples and the test project target. .NET 9: not built separately; NuGet's asset selection is the same rule that chose `net8.0` for .NET 10 |
| .NET Framework 4.7.2, 4.8, 4.8.1 | `netstandard2.0` | **Supported. Recommended** for .NET Framework apps | Restored and **compiled** against the packed `.nupkg` (net472, net48, net481). **Not executed** on .NET Framework. The full test suite passes against the same `netstandard2.0` assembly, run on the .NET 8 runtime. The Windows CI legs are defined and **not yet run** ([details](#what-was-verified-and-what-was-not)) |
| **.NET Framework 4.6.2, 4.7, 4.7.1** | `netstandard2.0` | **Supported. This is the declared floor.** 4.6.2 stays supported by Microsoft only until **Jan 12, 2027**; after that the floor rises to 4.7.2 unless the owners decide otherwise. Requires C# 9+, the TLS 1.2 setting for apps targeting below 4.7, and binding redirects | 4.6.2: restored and **compiled** against the packed `.nupkg` (net462), and the SDK's own source compiles with 0 errors against the 4.6.2 reference assemblies, so every framework API the SDK calls exists there. **Not executed** on 4.6.2. 4.7 and 4.7.1: not built separately. Same test-suite and CI-leg caveats as the row above |
| .NET 6, .NET 7 | `netstandard2.0` | Works, not supported. Out of Microsoft support since Nov 12, 2024 (.NET 6) and May 14, 2024 (.NET 7) | .NET 6: restored and compiled, not executed; the .NET SDK warns `NETSDK1138`. .NET 7: not built separately |
| .NET Framework 4.6.1 and lower | none | **Unsupported.** Microsoft ended support for 4.5.2, 4.6 and 4.6.1 on Apr 26, 2022, and `System.Text.Json` 8 does not support net461 | 4.6.1 restores and compiles, but `System.Text.Json`, `System.Text.Encodings.Web` and `Microsoft.Bcl.AsyncInterfaces` each emit a build warning that they do not support net461 and have not been tested with it. Below 4.6.1 there is no `netstandard2.0` support, so no compatible asset; not tried here |
| .NET Core 2.0 to 3.1, .NET 5 | `netstandard2.0` | **Unsupported** (untested; out of Microsoft support) | Not tested |
| Mono, Xamarin, UWP, Unity, Native AOT, trimming | none | **Unsupported** (untested) | Not tested |
| ASP.NET Core 8 and later | `net8.0` | **Supported** on .NET 8, 9 and 10 (same dates as above) | As .NET 8 and .NET 10 above. The sample web app runs on .NET 8 |
| ASP.NET Core 2.3 on .NET Framework | `netstandard2.0` | **Supported** at the .NET Framework floor above. Microsoft ends support for ASP.NET Core 2.3 on .NET Framework on **Apr 13, 2027** | Not built or run as an ASP.NET Core 2.3 host; it is a .NET Framework host and resolves the same `netstandard2.0` asset |
| Classic ASP.NET (WebForms, MVC 5, Web API 2) on .NET Framework | `netstandard2.0` | **Supported** on .NET Framework 4.6.2 and later, same rules as above | Not built or run as a classic ASP.NET host; same `netstandard2.0` asset and same evidence as the .NET Framework rows |

**What "Supported" means here.** If you target a supported platform and something
does not work, it is a bug we will fix. If you target one marked *Works, not
supported*, it may well work, but we have not committed to it and will not put
effort into platform-specific fixes. On *Unsupported*, expect nothing.

**"Supported" is our commitment, not a claim that it was executed.** Nothing on .NET
Framework was executed, and neither was .NET 6 or .NET 9. Support for .NET Framework
rests on four things together: the assembly compiles against it, the SDK's own source
compiles against the 4.6.2 reference assemblies, the same assembly passes the full
test suite on CoreCLR, and the behaviour that differs on .NET Framework (TLS defaults,
binding redirects) is documented below. It has not been run there. If that matters
to your release process, run your own smoke test on a real .NET Framework machine
before you ship, on the version you actually target (4.6.2 if that is your floor).

**Where the support statement depends on Microsoft's dates.** Every row that says
"Supported" for a runtime Microsoft is about to stop supporting is dated in the
[Support calendar](#support-calendar). Those rows are correct today and will not
stay correct without an owner revisiting them.

---

## Support calendar

Dated cliffs, in order. Each row says what changes in *this SDK's* declared support
when Microsoft's date passes. **The owners must revisit this page before each date**,
not after: on the day, the rows below are already wrong. Dates are as published on
the Microsoft pages under [Sources](#sources); re-check them when you revise.

| Date | What Microsoft ends | What changes in this SDK's declared support |
|---|---|---|
| **Nov 10, 2026** | .NET 8 and .NET 9 | The `net8.0` asset keeps working, but .NET 8 and .NET 9 move from *Supported* to *Works, not supported*, the same treatment .NET 6 and 7 get today. The supported minimum on modern .NET becomes **.NET 10**. The samples (which target .NET 8) should move to `net10.0` |
| **Jan 12, 2027** | .NET Framework 4.6.2 | The declared .NET Framework floor rises from 4.6.2 to **4.7.2** unless the owners decide otherwise. 4.6.2, 4.7 and 4.7.1 would move to *Works, not supported*. Note that 4.7 and 4.7.1 have no end date of their own (see below), so moving the floor to 4.7.2 is our policy choice, not something Microsoft's calendar forces |
| **Apr 13, 2027** | ASP.NET Core 2.3 on .NET Framework | The ASP.NET Core 2.3 host moves from *Supported* to *Works, not supported*. The classic ASP.NET and .NET Framework rows are unaffected: they follow the .NET Framework policy, not this one |
| **Nov 14, 2028** | .NET 10 | .NET 10 stops being a supported platform. The modern-.NET minimum becomes whichever .NET release is then in Microsoft support, after the package has been verified on it |

**No end date.** .NET Framework 4.7, 4.7.1, 4.7.2, 4.8 and 4.8.1 have no end date of
their own: Microsoft's lifecycle page says ".NET Framework 4.5.2 and later follows
the lifecycle policy of the underlying Windows OS on which it is installed." That is
why 4.7.2 is the recommended target and the natural next floor. Classic
ASP.NET on .NET Framework (WebForms and so on) follows the .NET Framework policy;
ASP.NET Core on .NET follows the .NET policy.

**On the Jan 12, 2027 timestamp.** Microsoft's lifecycle table shows the 4.6.2 end
as `1/13/2027 6:59:59 AM`, the same cutoff the support policy pages give as Jan 12,
2027. This page uses **Jan 12, 2027**; plan to be off 4.6.2 before then.

**Already passed** (for reference; each is why the row above is unsupported or
*Works, not supported*):

| Ended | What |
|---|---|
| Aug 21, 2021 | .NET Core 2.1 |
| Apr 26, 2022 | .NET Framework 4.5.2, 4.6, 4.6.1 |
| May 10, 2022 | .NET 5 |
| Dec 13, 2022 | .NET Core 3.1 |
| May 14, 2024 | .NET 7 |
| Nov 12, 2024 | .NET 6 |

---

## What was verified, and what was not

Everything in the first table was checked against the packed `AmwalPay.Ecr` 1.0.0
`.nupkg` (not the source tree), on macOS. **macOS can compile, audit and run tests on
CoreCLR; it cannot run .NET Framework.**

**Verified**

| Check | Result |
|---|---|
| A consumer project restored against the `.nupkg` | .NET Framework 4.6.1, 4.6.2, 4.7.2, 4.8, 4.8.1 and .NET 6 resolved the `netstandard2.0` asset; .NET 8 and .NET 10 resolved the `net8.0` asset |
| That consumer compiled | net461, net462, net472, net48, net481, net6.0, net8.0, net10.0 |
| That consumer ran | .NET 8 and .NET 10 only: a real sale against a signed fake terminal, answer `Approved` |
| The SDK's own C# source compiled directly against the .NET Framework 4.6.1, 4.6.2 and 4.7.2 reference assemblies | 0 errors each. Every framework API the SDK calls exists on 4.6.2. This is a compile-time check, not a run |
| The complete SDK test suite (410 tests at 1.0.0) against the `netstandard2.0` assembly, on the .NET 8 runtime | All pass, with `System.Text.Json` **8.0.6** resolved (re-run after the floor moved from 8.0.5). This is the assembly .NET Framework and .NET 6 consumers use, executed on CoreCLR, **not** on .NET Framework |
| The test project itself | Targets `net8.0` by default on every OS, so a plain `dotnet test` exercises the `net8.0` asset |
| Blocking callers | `SaleAsync(...).GetAwaiter().GetResult()` from a single-threaded `SynchronizationContext` over real loopback TCP, the lost-answer recovery path, a timeout and the Web Service path all complete. Mutation-tested: removing one `ConfigureAwait(false)` makes three of the five deadlock, so the tests would catch it |
| `dotnet list package --vulnerable` and NuGet's advisory audit | `System.Text.Json` 8.0.5 and 8.0.6 audit clean; 8.0.4 and lower are flagged High |
| `net461` build warnings | Three (see the table above). `net462` and later: none |
| Language version | LangVersion 7.3 fails with `CS8370`, 8.0 with `CS8400`, 9.0 compiles ([details](#c-9-or-later)) |

**Defined, not run.** These CI legs exist in the repository. **None has been run on
Windows yet**, so none of them is evidence today:

| Leg | What it will show | Status |
|---|---|---|
| The test project also targets `net462` and `net472` on Windows (`dotnet test tests\AmwalPay.Ecr.Tests -f net462`, `-f net472`), in the `ecr-sdk-windows-tests` workflow in `codemagic.yaml` | The suite executing the `netstandard2.0` build on the .NET Framework runtime installed on the runner | **Defined, not run** |
| A `net10.0` test target, added automatically when the .NET SDK is 10 or newer | The suite on the .NET 10 runtime | **Defined, not run** |
| A compile gate: `dotnet build src/AmwalPay.Ecr -p:VerifyNetFramework=true` builds the SDK's own source for `net462` (nothing built this way is packed or published) | A future use of an API missing on .NET Framework 4.6.2 breaks the build | **Defined, not run** |

**What the .NET Framework legs cannot prove.** .NET Framework 4.x versions are
in-place updates: a Windows runner with 4.8 installed executes the `net462`-targeted
tests on the **4.8 runtime**. That validates how the `netstandard2.0` build behaves on
.NET Framework; it cannot prove 4.6.2's own runtime behaviour. Until you have run it on a machine that
really is at 4.6.2, "supported on 4.6.2" means "compiles there and behaves on .NET
Framework 4.x", not "executed on 4.6.2".

**Not verified in this repository's test runs**

- Anything **executed** on .NET Framework (any version), on .NET 6, or on .NET 9.
- Real WinForms, WPF or classic ASP.NET synchronisation contexts. The blocking
  tests use a purpose-built single-threaded context that behaves the same way;
  they do not host the real thing.
- TLS behaviour and binding-redirect behaviour on .NET Framework. Both are stated
  from Microsoft's documented behaviour (see [.NET Framework](#net-framework)).
- Load, or many concurrent callers. The claim that one instance is safe to share
  is from reading the source (no connection and no mutable state is held between
  calls); it has not been load-tested.
- Linux and Windows hosts. Verification ran on macOS. The package contains no
  P/Invoke and no operating-system-specific API, but that is a statement about the
  code, not a test run on those systems.
- ASP.NET Core 2.3 on .NET Framework and classic ASP.NET as hosts.
- Native AOT and trimming.

---

## What is in the package

Two assets. NuGet picks the best one for your project. **There is no `net462`
asset**: shipping one would change what every .NET Framework app resolves, so the
`net462` compile gate above builds the same source only to check it, and packs nothing.

**`lib/net8.0`: zero package dependencies.** Used by .NET 8 and later. Nothing else
is added to your project.

**`lib/netstandard2.0`: depends on `System.Text.Json` 8.0.6.** Used by .NET
Framework (4.6.2 and later) and by .NET 6 and 7. This is the graph a real .NET
Framework 4.6.2 consumer restored against the packed `.nupkg` (checked, not read from
metadata): nine runtime packages, the same set for 4.7.2 and 4.8.

| Package | Version |
|---|---|
| `System.Text.Json` | 8.0.6 (direct; the declared floor) |
| `Microsoft.Bcl.AsyncInterfaces` | 8.0.0 |
| `System.Buffers` | 4.5.1 |
| `System.Memory` | 4.5.5 |
| `System.Numerics.Vectors` | 4.5.0 (arrives through `System.Memory`) |
| `System.Runtime.CompilerServices.Unsafe` | 6.0.0 |
| `System.Text.Encodings.Web` | 8.0.0 |
| `System.Threading.Tasks.Extensions` | 4.5.4 |
| `System.ValueTuple` | 4.5.0 (**.NET Framework projects only**: it is in the `.NETFramework4.6.2` dependency group of the `System.Text.Json` package, not in the `netstandard2.0` group) |

`dotnet list package --vulnerable --include-transitive` reports no advisories for this
whole set, and the 4.6.2 and 4.7.2 consumers build with 0 warnings. Fewer of these
packages are needed on .NET 6/7, which supply some of them themselves.

**Why 8.0.6.** NuGet's advisory audit flags `System.Text.Json` 8.0.4 and lower as
High; 8.0.5 and 8.0.6 are clean. 8.0.6 was the latest 8.x when the floor was set. A
project can choose a newer version. The 6.x line, whose last version with a
`.NETFramework4.6.1` group is 6.0.11, is out of support and not used.

`System.Text.Json` is an internal detail. It appears on the public API in exactly
two places, `EcrWireResponse.Data` and the return values of
`EcrWebServiceRequestJson`, so it does not clash with your own JSON library.

---

## C# 9 or later

Your project must compile with **C# 9.0 or later**. `EcrConfig` and other public
types use init-only setters, so setting them from your code needs the C# 9
`init` feature, and `with` expressions on the record types need C# 9 as well.

The compiler tells you plainly when the language version is too low. These are the
errors that were reproduced:

| `LangVersion` | Result |
|---|---|
| 7.3 | `CS8370`: Feature 'init-only setters' is not available in C# 7.3. Please use language version 9.0 or greater |
| 8.0 | `CS8400`: Feature 'init-only setters' is not available in C# 8.0. Please use language version 9.0 or greater |
| 9.0 | Compiles |

**Who is affected.** SDK-style projects on .NET 5 or later default to a language
version that is already high enough. **SDK-style .NET Framework projects default to
C# 7.3**, so they fail until you set the version:

```xml
<PropertyGroup>
  <LangVersion>9.0</LangVersion>   <!-- or: latest -->
</PropertyGroup>
```

Use Visual Studio 2019 16.8 or later, or the .NET SDK 5 or later, to build. An older
compiler does not know C# 9 whatever you set.

**What only needs C# 7.** Reading results does not need C# 9. Pattern matching
such as `is EcrResult.Approved a` and `switch` over the result types compiles with C# 7.
It is *constructing* an `EcrConfig` with an object initializer, and `with`, that
need 9.

---

## .NET Framework

**Target 4.6.2 or later; prefer 4.7.2 or later.** 4.6.2 is the declared floor and
4.7.2 is the version we recommend for anything new.

**Why 4.6.2 is the floor.** It is the oldest .NET Framework that clears all three
bars at once:

- **Microsoft still supports it**, until Jan 12, 2027. 4.5.2, 4.6 and 4.6.1 ended
  on Apr 26, 2022.
- **`System.Text.Json` supports it.** Microsoft's overview of the package lists
  ".NET Framework 4.6.2 and later", and the 8.0.5 and 8.0.6 packages have a
  `.NETFramework4.6.2` dependency group.
- **`netstandard2.0` supports it.** Microsoft's *.NET Standard* page lists 4.6.2 and
  later for `netstandard2.0`, without the caveat it attaches to 4.6.1.

**Why 4.6.1 is not supported.** Its own dependencies say so: `System.Text.Json`
8.0.x, `System.Text.Encodings.Web` 8.0.0 and `Microsoft.Bcl.AsyncInterfaces` 8.0.0
each emit the build warning "doesn't support net461 and has not been tested with
it. Consider upgrading your TargetFramework to net462 or later." net462 and later
emit none. Microsoft's *.NET Standard* page adds that although NuGet treats 4.6.1
as supporting `netstandard2.0`, "there are several issues with consuming .NET
Standard libraries that were built for those versions from .NET Framework 4.6.1
projects", and recommends upgrading to 4.7.2 or higher.

**Why 4.7.2 is recommended.** That footnote's advice is written for 4.6.1 and it
names 4.7.2 or higher, so it is the safest reading of Microsoft's guidance for
`netstandard2.0` consumers; it does not say 4.6.2 is broken. Beyond that, 4.7.2 has
no end date (it follows the Windows OS lifecycle), and an app that targets 4.7 or
later uses the operating system's TLS defaults, so it needs no manual TLS setting
(below). 4.6.2 works today and the calendar will end it.

**4.6.2 and the calendar.** Microsoft ends support for 4.6.2 on **Jan 12, 2027**.
After that date the declared floor rises to 4.7.2 unless the owners decide
otherwise; see the [Support calendar](#support-calendar). If you are starting a
project now, target 4.7.2 or 4.8 and you never have to move.

**No `net462` asset.** The SDK does not ship a `net462` build. A 4.6.2 app gets the
`netstandard2.0` build, and with it `System.Text.Json` 8.0.6 and the graph in
[What is in the package](#what-is-in-the-package). A project that targets 4.6.1
restores the same asset and gets the three `net461` warnings.

The rest of this section describes documented .NET Framework behaviour. **It was
not exercised in this repository's test runs**; treat it as guidance from
Microsoft's documentation, not as something we reproduced.

### Binding redirects

The `netstandard2.0` asset brings the `System.*` assemblies listed above, and .NET
Framework resolves assembly versions strictly. The version your app compiled
against and the version that ends up next to it can differ, and .NET Framework
then refuses to load the assembly unless a **binding redirect** says the newer
one is acceptable.

- **SDK-style projects** generate redirects for you (the
  `AutoGenerateBindingRedirects` property, on by default for executables). Nothing to do.
- **Classic `packages.config` projects** may need them by hand. In Visual Studio,
  run `Add-BindingRedirect` in the Package Manager Console, or add the entries to
  `app.config` (`web.config` for a web app).
- **The redirect belongs in the executable or web project**, not in a class library
  that references the SDK. A library's own config file is not read.

If you see `FileLoadException` or "Could not load file or assembly", see
[troubleshooting](troubleshooting.md#could-not-load-file-or-assembly-fileloadexception-on-a-classic-net-framework-project).

### TLS

- **LAN ECR involves no TLS.** It is plain TCP to the terminal.
- **Web Service ECR is HTTPS to the Amwal Hub and needs TLS 1.2.**
- An app that **targets .NET Framework 4.7 or later** uses the operating system's
  default TLS versions. An app that **targets a version before 4.7** (which
  includes the 4.6.2 floor) defaults to older protocols (this is Microsoft's
  documented behaviour; see the .NET Framework guidance titled
  *Transport Layer Security (TLS) best practices with the .NET Framework*). It is
  the **app's** target that counts, not this library's.

In an app that targets a version before 4.7, before the first Web Service call:

```csharp
ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
```

A very old Windows (7 or Server 2008 R2) additionally needs its own TLS 1.2 update.
The SDK sets no TLS option itself; this is always the host application's job.

### Blocking calls (`.Result`, `.Wait()`)

Legacy code often blocks on async calls, and on a UI thread or classic ASP.NET
request thread that deadlocks any library that forgets `ConfigureAwait(false)`.
This library does not. The regression suite blocks on `SaleAsync(...)` from a
single-threaded synchronisation context, the shape of a WinForms or WPF UI thread
or a classic ASP.NET request, and it completes on every path tested (LAN,
recovery after a lost answer, timeout, Web Service). It has not been run under the
real WinForms, WPF or ASP.NET contexts, only under a context built to behave the
same way.

That is a statement about deadlocks, not about a good idea. A sale takes as long as
the cardholder takes, so a blocked UI thread is a frozen window for all of it, and a
blocked web request holds a thread. **Async all the way is still the recommendation.**

---

## Windows native apps

WinForms, WPF, console apps and Windows Services all use the same package:
**.NET Framework 4.6.2 or later** (4.7.2 or later recommended) picks the
`netstandard2.0` asset, **.NET 8 or later** picks `net8.0`. There is no
Windows-specific requirement on the package itself: it is OS-neutral managed code.
Only the WPF **sample** is Windows-only.

Things your app takes on, not the package:

- **Storing keys.** DPAPI (`ProtectedData`) is Windows-only and is the
  [integration guide's](integration-guide.md#storing-keys-with-dpapi)
  recommendation for tills. The SDK does not call it.
- **Windows Firewall and network profile** for LAN ECR, covered in
  [troubleshooting](troubleshooting.md#windows-network-and-host).
- **The support calendar.** A till on .NET Framework 4.6.2 or on .NET 8 is
  supported today and has a Microsoft end date; see the
  [Support calendar](#support-calendar).

A working WPF sample on .NET 8 is in
[`Windows-Native-Example`](https://github.com/amwal-pay/AmwalpayEcrWindowsExample/tree/main/Windows-Native-Example).
It targets .NET 8, which Microsoft stops supporting on Nov 10, 2026; the sample
should move to `net10.0`.

---

## Web apps

| Host | Use | Asset |
|---|---|---|
| ASP.NET Core on .NET 8, 9 or 10 | Supported (Microsoft ends .NET 8 and 9 on Nov 10, 2026; .NET 10 on Nov 14, 2028) | `net8.0`, no package dependencies |
| ASP.NET Core 2.3 on .NET Framework | Supported at the .NET Framework floor; Microsoft ends it on Apr 13, 2027 | `netstandard2.0` |
| Classic ASP.NET (Web API 2, MVC 5, WebForms) on .NET Framework 4.6.2 or later | Supported, see [.NET Framework](#net-framework) | `netstandard2.0` |

ASP.NET Core on .NET follows the .NET support policy; classic ASP.NET on .NET
Framework follows the .NET Framework policy. Both are dated in the
[Support calendar](#support-calendar).

A working ASP.NET Core sample on .NET 8 is in
[`AmwalECR-Web-Example`](https://github.com/amwal-pay/AmwalpayEcrWindowsExample/tree/main/AmwalECR-Web-Example).
It targets .NET 8, which Microsoft stops supporting on Nov 10, 2026; the sample
should move to `net10.0`.

**Which mode fits a server.** LAN ECR opens a TCP connection from your server to
the terminal's IP on port 9100, so the server must be able to route to the terminal.
A server in a data centre cannot reach a terminal on a shop's Wi-Fi. Use **Web
Service ECR** (HTTPS to the Hub) when the app is hosted away from the shop, and LAN
when the server sits on the same network.

**Sharing and lifetime.** Both are safe to call concurrently and to hold as
singletons, because neither keeps state between calls (from reading the source;
not load-tested):

- **LAN (`EcrTerminal`)** opens **one TCP connection per call** and keeps none
  between calls.
- **Web Service (`EcrWebServiceTerminal`)** uses **one process-wide static
  `HttpClient`**, so creating one per request does not exhaust sockets either.
- `EcrOpenedSession` (from `EcrSessions.Open`) wraps either; the same holds.

So register one per physical terminal and keep no per-request state on it. The
sample web app registers its transaction engine as a singleton and opens a session
per transaction from stored terminal settings; both shapes are fine.

```csharp
// ASP.NET Core (.NET 8): register once at start-up.
builder.Services.AddSingleton<EcrTerminal>(sp =>
{
    var section = sp.GetRequiredService<IConfiguration>().GetSection("Ecr");
    var plan = EcrSessions.Plan(
        link: new EcrLink.Lan(Host: section["TerminalHost"] ?? ""),
        config: new EcrConfig { SecureHashKey = section["LanKey"] ?? "" });

    if (!plan.IsReady)   // fail at start-up, not on the first customer
        throw new InvalidOperationException("ECR is not configured: " + string.Join("; ", plan.Issues));

    return EcrSessions.LanTerminal(
        terminalSerial: section["TerminalSerial"] ?? "",
        plan: plan);
});
```

The signing key comes from your secret store (user secrets, a vault, environment
variables), never from source or a committed `appsettings.json`. For Web Service,
register `EcrSessions.WebServiceTerminal(...)` the same way; the
[integration guide](integration-guide.md#running-in-a-web-app)
has a complete, compile-checked example including the endpoint.

**What a web app must still respect**

- **Do not pass `HttpContext.RequestAborted` to a money-moving call.** Cancelling a
  token stops the SDK *waiting*; it does not stop the payment. A shopper closing a
  tab must not turn a sale still running at the terminal into an unknown outcome
  nobody is watching. Pass your own `merchantReference` so you can inquire later.
- **The terminal serves one money-moving transaction at a time.** Two concurrent
  sales to the same terminal collide: the second gets `96`. Serialise sales per
  terminal in your app. Inquiries are answered while busy.
- **Never treat a lost answer as a decline.** Same rule as everywhere; see
  [the one rule](../README.md#the-one-rule).

---

## Not part of the support statement

- **USB cable channel.** The package does not ship one. `EcrLink.UsbCable` and
  `EcrSessions.Open(..., usbChannel: ...)` accept a channel you supply.
- **Native AOT and trimming.** Untested. The package does not declare itself
  trim- or AOT-compatible.
- **Mono, Xamarin, UWP, Unity.** Untested.
- **A `net462` asset.** Not shipped; see [What is in the package](#what-is-in-the-package).
- **Runtimes out of support upstream** (.NET Framework 4.6.1 and lower, .NET 6 and
  7, .NET Core 3.1 and older, .NET 5).
- **Operating systems.** The package has no OS requirement, but only macOS was
  used for verification, and the platform matrix above is about .NET versions, not
  operating systems.

Found a platform that behaves differently from this page? Include the target
framework, the exact error or warning text, and the output of `dotnet --info`, and
open an issue on
[GitHub](https://github.com/amwal-pay/AmwalpayEcrWindows/issues).

---

## Sources

Every platform and date on this page comes from these pages or from NuGet package
metadata, plus the verification described above.

| Source | Used for |
|---|---|
| [Serialize and deserialize JSON using C# (System.Text.Json overview)](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview) | `System.Text.Json` package support: ".NET Standard 2.0 and later", ".NET Framework 4.6.2 and later", ".NET 8 and later"; built in from .NET Core 3.0 |
| [.NET Standard overview](https://learn.microsoft.com/en-us/dotnet/standard/net-standard) | Which runtimes support `netstandard2.0`; the .NET Framework 4.6.1 caveat and the 4.7.2 recommendation; multi-targeting `netstandard2.0` plus .NET 5 and later for reusable libraries |
| [Microsoft .NET Framework lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework) | .NET Framework end dates, including 4.6.2 |
| [.NET Framework support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework) | .NET Framework end dates; 4.5.2 and later follow the underlying Windows OS |
| [.NET and .NET Core support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) | End dates for .NET Core, .NET 5 to 10 |
| [ASP.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/aspnet) | ASP.NET Core 2.3 on .NET Framework end date |
| `System.Text.Json` package metadata on nuget.org (8.0.5, 8.0.6) and NuGet's advisory audit | Dependency groups per framework; advisory status of 8.0.4 and lower |
