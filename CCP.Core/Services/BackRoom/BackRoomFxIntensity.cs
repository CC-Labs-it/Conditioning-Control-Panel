namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>Setting <c>AppSettings.BackRoomFxIntensity</c> (CONTRACT section 4). Default
/// <see cref="Normal"/>. <see cref="Calm"/> is also forced whenever MotionLevel is not Full.
/// Full never breaks the Brake: no strobe over 6 Hz, one hero at a time, toggles still win.
///
/// <para>Split out of <c>BackRoomContracts.cs</c> because <see cref="Models.AppSettings"/>
/// persists this value and settings reached Core before the rest of the Back Room contract
/// surface did; the namespace is unchanged, so no caller moves.</para>
/// </summary>
public enum BackRoomFxIntensity
{
    Calm,
    Normal,
    Full,
}
