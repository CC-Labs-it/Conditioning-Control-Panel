This small headless host links the shipped AssetsTabView pack-hover partial, CardSheenAdorner, FxAdorner, EffectiveVisibility, FrameClock and Core FrameGate directly. Only the unrelated AmbientFxCanvas environment inputs are isolated. The fixture uses the production outer card markup; a test checks it still matches the production file copied into the output. This does not substitute for the full AssetsTabTests shell fixture.

Refresh the fixture after intentional template changes:

```sh
python3 Tests/CCP.Avalonia.PackCardFx.Tests/generate_markup.py
dotnet build Tests/CCP.Avalonia.PackCardFx.Tests -c Release -m:1 -p:UseSharedCompilation=false
dotnet Tests/CCP.Avalonia.PackCardFx.Tests/bin/Release/net10.0/CCP.Avalonia.PackCardFx.Tests.dll
```

To reproduce the missing pointer-handler regression, extract AssetsTabView.axaml from the brief BASE, pass that temporary file to generate_markup.py, rebuild, and run only `-method PackFxProof.PointerMovesOneSheenAndLateLeaveDoesNotStopTheNewCard`. It must fail with an empty adorner collection. Then regenerate from the production template, rebuild and run all tests. The focused pre-fix run excludes the fixture-consistency assertion because the output also contains the current production template.

The production-source tests use the existing shared Avalonia dispatcher and a real headless Window, adorner layer, pointer routed events and frame clock. They stop the clock before closing the fixture. They write no real profile data.
