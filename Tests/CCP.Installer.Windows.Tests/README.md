# Open with CCP installer test

`file_association_installer_test.py` runs the compiled setup as a silent per-user install (`/CURRENTUSER`, no UAC) on the same kind of disposable VM, without an existing CCP installation. Install the VC++ x64 and WebView2 runtimes first: if VC++ is missing, setup runs `VC_redist.x64.exe` (installer-avalonia.iss `VCRedistNeeded`), which asks for elevation and stalls the silent run. It seeds another application's `OpenWithProgids` value for every extension, then checks: opt-in (`/TASKS=fileassoc`) writes the 34 values of WPF `installer.iss:216-265`; opening a file through each ProgID's shell verb (`ShellExecuteEx` with the class, as Explorer's Open With does) starts the installed exe with `--play` or `--edit` and the file; an upgrade without the task removes them; opting in again restores them; uninstall removes them. Each step also checks the foreign value and the extensions' default application are unchanged. Whether the app then opens the player or editor is not checked here.

```powershell
python Tests/CCP.Installer.Windows.Tests/file_association_installer_test.py --disposable-vm --setup C:\test\setup.exe --workspace C:\test\workspace
```

The portable checks (`test_file_association_validator.py`) run anywhere: `python3 -m unittest discover -s Tests/CCP.Installer.Windows.Tests -p 'test_*.py'`.
