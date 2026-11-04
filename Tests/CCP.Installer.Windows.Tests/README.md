# Browser reset installer tests

These tests launch the actual compiled `installer-avalonia.iss` setup, not a model of its Pascal code. They are for a **disposable Windows VM only**: setup modifies the app installation, registry, and shortcuts and may install runtime prerequisites. Do not use a machine with an existing CCP installation or real profile data.

Build the installer from this checkout with Inno Setup as in `.github/workflows/build.yml`. Install the VC++ and WebView2 runtimes on the VM first. Use a normal desktop session with Python 3.8 or later, two local accounts, and a test workspace writable by both. The original user must be able to create and remove `browser_data` in both accounts' otherwise empty CCP profile folders; configure access on the disposable VM. The harness refuses to overwrite an existing browser profile or reset marker. Disable any automatic app launch and uncheck the launch option at the end of setup.

Run each combination of `--mode interactive|silent`, `--identity same|different`, and `--data default|override` (eight invocations). For example, from the original user's desktop:

```powershell
python Tests/CCP.Installer.Windows.Tests/browser_reset_installer_test.py --disposable-vm --setup C:\test\setup.exe --workspace C:\test\workspace --mode interactive --identity same --data override
python Tests/CCP.Installer.Windows.Tests/browser_reset_installer_test.py --disposable-vm --setup C:\test\setup.exe --workspace C:\test\workspace --mode interactive --identity different --data default --setup-sid S-1-5-21-... --setup-localappdata C:\Users\OtherAdmin\AppData\Local
```

For `same`, launch from an administrator's unelevated desktop and approve elevation for that same account. For `different`, launch from the original user's desktop, select installation for all users, and enter the other administrator's credentials only in the Windows UAC prompt. Obtain that administrator's SID in its own session with `whoami /user`; it is an account identifier, not a password. Supply the other account's actual `LOCALAPPDATA` path and SID for all `different` runs, including override runs. The harness observes the setup wizard process token and fails if it cannot verify the expected SID and elevation; it does not handle or store credentials.

Complete the interactive wizard normally. For silent cases, the production installer uses its previous installation privileges: first make an all-users installation under the intended identity, then run the silent case from the original user's session and complete any Windows elevation prompt. Each test checks the actual original-user command's logged exit status, both profiles' marker files, and the override target. Interactive cases require exit 0 for matching users or 1 for different users; a failed command is a failure, not a match. Silent cases require a pending marker and no acknowledgment. The installer log stays in the newly created test workspace directory for inspection. These tests require a desktop and UAC interaction and are not automatically run by Linux or headless CI.

WPF stores the prior browser profile under `browser_data` (ConditioningControlPanel/Services/Browser/BrowserService.cs:125-132). Its updater runs setup silently (ConditioningControlPanel/Services/Update/UpdateService.cs:905-914 and CCP.Core/Services/Update/ReleaseFeed.cs). The separate Avalonia profile makes the reset notice necessary.

Portable tests of the result validator can run anywhere:

```sh
python3 -m unittest discover -s Tests/CCP.Installer.Windows.Tests -p 'test_*.py'
```

They do not execute Inno Setup or validate Windows identities.
