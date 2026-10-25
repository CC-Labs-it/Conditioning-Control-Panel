# Browser popup and fallback tester steps

1. WPF `ConditioningControlPanel/Services/Browser/BrowserService.cs:532-552` Rows: `shell-browser`
   From a non-partner page in the browser card, open a normal link in a new window:
   it must navigate the existing host without creating another OS window. Ad-domain and
   `/popup` targets must do nothing. From HypnoTube (including subdomains), the partner
   exception must navigate in place even for an ad-pattern target. Repeat while popped out.
2. WPF `ConditioningControlPanel/MainWindow/MainWindow.Browser.cs:473-485` Rows: `shell-browser`
   No caller yet: nothing in this head calls NavigateToUrlInBrowser, so a tester cannot
   reach the no-engine HTTPS fallback. Skip until a link caller lands (covered headless by
   BrowserModNavigationTests). Pending-adapter timeout/retry remains stubbed.
