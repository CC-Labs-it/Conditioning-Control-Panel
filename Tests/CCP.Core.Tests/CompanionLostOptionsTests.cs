using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace CCP.Core.Tests;

// fd910ec33: follow each lost option from a registered page through its host to its control.
// These source contracts run without a UI platform; CompanionPagesTests exercises live adoption.
public sealed class CompanionLostOptionsTests
{
    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository not found");
        }
    }
    private static string Read(string file) => File.ReadAllText(Path.Combine(Root, "CCP.Avalonia", file));
    private const string Companion = "Views/Controls/Companion/";
    private static void Named(string file, string name) => Assert.Contains(
        XDocument.Parse(Read(file)).Descendants(), e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);

    [Theory]
    [InlineData("personality", "Personality", "CompanionPersonalityTab")]
    [InlineData("permissions", "Permissions", "CompanionPermissionsTab")]
    [InlineData("companionlinks", "Links", "CompanionLinksTab")]
    [InlineData("companionai", "Ai", "CompanionAiTab")]
    public void PagesAreMountedAndShown(string key, string page, string host)
    {
        Named("Views/Windows/MainShellWindow.axaml", host);
        var nav = Read("Views/Windows/MainShellWindow.TabNavigation.cs");
        Assert.Contains($"[\"{key}\"] = \"{host}\"", nav);
        Assert.Contains($"{page}Page>(\"{host}\")?.OnShown(RoomView())", nav);
    }

    [Theory]
    [InlineData("Ai", "Behavior", "BehaviorHost", "WorkshopBehaviorCell", "SliderBubbleDurationCompanion")]
    [InlineData("Ai", "Behavior", "BehaviorHost", "WorkshopBehaviorCell", "SliderIdleIntervalCompanion")]
    [InlineData("Ai", "Behavior", "BehaviorHost", "WorkshopBehaviorCell", "ChkVoiceLinesCompanion")]
    [InlineData("Ai", "Behavior", "BehaviorHost", "WorkshopBehaviorCell", "ChkTubeMidnightGlass")]
    [InlineData("Ai", "Behavior", "BehaviorHost", "WorkshopBehaviorCell", "ChkMuteWhispersCompanion")]
    [InlineData("Ai", "Behavior", "BehaviorHost", "WorkshopBehaviorCell", "ChkPauseBrowserCompanion")]
    [InlineData("Ai", "Triggers", "TriggersHost", "WorkshopTriggersCell", "ChkTriggerModeCompanion")]
    [InlineData("Ai", "Triggers", "TriggersHost", "WorkshopTriggersCell", "SliderTriggerIntervalCompanion")]
    [InlineData("Personality", "Community", "CommunityHost", "WorkshopCommunityCell", "BtnBrowsePrompts")]
    [InlineData("Personality", "Community", "CommunityHost", "WorkshopCommunityCell", "BtnImportPrompt")]
    [InlineData("Personality", "Community", "CommunityHost", "WorkshopCommunityCell", "BtnExportPrompt")]
    [InlineData("Personality", "Community", "CommunityHost", "WorkshopCommunityCell", "BtnRefreshPrompts")]
    [InlineData("Links", "Library", "VideosHost", "WorkshopLibraryCell", "BtnAddVideoLink")]
    public void PageAdoptsTheCellThatCarriesTheOption(string page, string part, string host, string cell, string control)
    {
        Named(Companion + $"Pages/{page}Page.axaml", host);
        Assert.Contains($"CompanionPageHost.Adopt(workshop.Parts.{part}, {host})", Read(Companion + $"Pages/{page}Page.axaml.cs"));
        Named(Companion + $"Runtime/{cell}.axaml", control);
    }

    [Fact]
    public void PickerPresetsPromptEditorPermissionsAndLinksHavePageHosts()
    {
        foreach (var name in new[] { "PickerHost", "PresetsHost", "BtnOpenPromptEditor" }) Named(Companion + "Pages/PersonalityPage.axaml", name);
        Named(Companion + "Pages/PermissionsPage.axaml", "PermissionsHost");
        Named(Companion + "AiPermissionsGrid.axaml", "ChkAllowLockCard");
        foreach (var file in new[] { Companion + "Pages/LinksPage.axaml", "Views/Dialogs/CompanionPromptEditorDialog.axaml" }) Named(file, "KnowledgeLinks");
        foreach (var name in new[] { "LstKnowledgeLinks", "BtnAddKnowledgeLink", "BtnRemoveKnowledgeLink" }) Named(Companion + "KnowledgeLinksEditor.axaml", name);
    }
}
