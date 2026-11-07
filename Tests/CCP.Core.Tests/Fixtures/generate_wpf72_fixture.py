"""Regenerate the WPF-model sample. Build CCP.Avalonia first; pass the dotnet executable.
No GUI, user profile, credentials or secret store is accessed.
"""
import json
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
REV = "aa9918833722f057079e455167cb3d1b14462340"
source = subprocess.check_output(["git", "show", REV + ":ConditioningControlPanel/Models/AppSettings.cs"], cwd=ROOT, text=True)
# Only platform hooks differ; preserve WPF's properties, initializers and JSON contract.
replacements = {
    "try { ConditioningControlPanel.App.Bark?.NotifySettingChanged(name); } catch { /* never break settings for a bark */ }": "CoreSettingsHooks.NotifySettingChanged(name);",
    "Services.Fyp.Online.FypOnlineCoordinator.SanitizeSub": "Services.Fyp.SubredditName.Sanitize",
    "Services.SecureApiKeyStore.Retrieve()": "CoreSecrets.Retrieve(CoreSecrets.ApiKey)",
    "Services.SecureApiKeyStore.Store(": "StoreApiKey(",
    "Services.SecureAuthTokenStore.Retrieve()": "CoreSecrets.Retrieve(CoreSecrets.AuthToken)",
    "Services.SecureAuthTokenStore.Store(": "StoreAuthToken(",
    "App.Mods?.GetModeDisplayName()": "CoreMods.ModeDisplayName",
    "public class AppSettings : INotifyPropertyChanged\n    {": "public class AppSettings : INotifyPropertyChanged\n    {\n private static void StoreApiKey(string? v) => CoreSecrets.Store(CoreSecrets.ApiKey,v);\n private static void StoreAuthToken(string? v) => CoreSecrets.Store(CoreSecrets.AuthToken,v);",
}
for old, new in replacements.items():
    assert old in source, old
    source = source.replace(old, new)
PROGRAM = r'''
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Models;
var root = args[0];
var input = JObject.Parse(File.ReadAllText(root + "Tests/CCP.Core.Tests/Fixtures/mod_activation_golden.json").Replace("stamped-during-run", "2026-10-01T12:00:00Z"));
// Model-derived established profile: onboarding and one-time migrations already completed.
foreach(var key in new[]{"Welcomed", "FirstRunAssetsPromptShown", "MarqueeNeutralDefaultMigrated", "CirceNeutralTextMigrated", "LoudnessThresholdRelaxed", "MigratedUnifiedOverlayHostOn", "MigratedCompositorOffThreadOn"}) input[key] = true;
foreach(var key in new[]{"HasSeenProgramsTab", "HasSeenProgramsIntro", "HasSeenDeeperTab", "HasSeededDeeperDemos", "HasSeenDeeperWelcome", "HasSeenDeeperEditorIntro", "HasSeenDeeperHTInteractiveTutorial"}) input[key] = true;
input["InstallDate"] = "2025-01-01";
input["LastSeenVersion"] = "7.1.15";
input["LastDailyQuestDate"] = "2025-03-12T23:00:00+00:00";
input["CompanionPerk"] = 0;
input["WhatMovedCardShown"] = 1;
input["SeenFeatureIntros"] = new JArray("daily-free", "one-account");
input["Haptics"] = new JObject { ["Enabled"] = false }; // no paid entitlement in this synthetic profile
// A valid user choice: these shipped presets have been removed, so startup must not re-add them.
input["RemovedBuiltInPresetIds"] = new JArray(Directory.GetFiles(root + "CCP.Avalonia/bin/Debug/net10.0/Resources/AwarenessPresets", "*.json").Select(p => JObject.Parse(File.ReadAllText(p))["id"]));
var settings = JsonConvert.DeserializeObject<AppSettings>(input.ToString(), new JsonSerializerSettings {ObjectCreationHandling = ObjectCreationHandling.Replace});
settings!.Haptics.EnsureV2Migrated();
File.WriteAllText(args[1], JsonConvert.SerializeObject(settings, Formatting.None));
'''
with tempfile.TemporaryDirectory(prefix="ccp-wpf-model-") as folder:
    temp = Path(folder)
    (temp / "AppSettings.cs").write_text(source)
    (temp / "Program.cs").write_text(PROGRAM)
    (temp / "generate.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>CCP.Avalonia.Tests</AssemblyName><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><NoWarn>0436</NoWarn></PropertyGroup><ItemGroup><ProjectReference Include="{ROOT}/CCP.Core/CCP.Core.csproj" /></ItemGroup></Project>''')
    output = temp / "settings.json"
    subprocess.run([sys.argv[1] if len(sys.argv) > 1 else "dotnet", "run", "--project", str(temp / "generate.csproj"), "--", str(ROOT) + "/", str(output)], check=True)
    data = json.loads(output.read_text())
    properties = [json.dumps(k) + ":" + json.dumps(v, ensure_ascii=False, separators=(",", ":")) for k, v in data.items()]
    # Eight top-level keys per line: reviewable while keeping the PR below 600 changed lines.
    lines = [",".join(properties[i:i+8]) for i in range(0, len(properties), 8)]
    Path(__file__).with_name("wpf72_settings.json").write_text("{\n" + ",\n".join(lines) + "\n}\n")
