
1. WPF `ConditioningControlPanel/Services/Browser/BrowserService.cs:532-552` Rows: `shell-browser`
   From a non-partner page in the browser card, open a normal link in a new window:
   it must navigate the existing host without creating another OS window. Ad-domain and
   `/popup` targets must do nothing. From HypnoTube (including subdomains), the partner
   exception must navigate in place even for an ad-pattern target. Repeat while popped out.
2. WPF `ConditioningControlPanel/MainWindow/MainWindow.Browser.cs:473-485` Rows: `shell-browser`
   With no native engine installed, invoke a browser link: HTTPS opens in the system
   browser; HTTP and file links do not. Enable offline mode: no external browser opens.
   Pending-adapter timeout/retry remains stubbed.
