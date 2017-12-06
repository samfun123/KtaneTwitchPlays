using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Assets.Scripts.Helpers;
using UnityEngine;

public class FreeplayCommander : ICommandResponder
{
    #region Constructors
    static FreeplayCommander()
    {
        _freeplayDeviceType = CommonReflectedTypeInfo.FreeplayDeviceType;
        _moduleCountIncrementField = _freeplayDeviceType.GetField("ModuleCountIncrement", BindingFlags.Public | BindingFlags.Instance);
        _moduleCountDecrementField = _freeplayDeviceType.GetField("ModuleCountDecrement", BindingFlags.Public | BindingFlags.Instance);
        _timeIncrementField = _freeplayDeviceType.GetField("TimeIncrement", BindingFlags.Public | BindingFlags.Instance);
        _timeDecrementField = _freeplayDeviceType.GetField("TimeDecrement", BindingFlags.Public | BindingFlags.Instance);
        _needyToggleField = _freeplayDeviceType.GetField("NeedyToggle", BindingFlags.Public | BindingFlags.Instance);
        _hardcoreToggleField = _freeplayDeviceType.GetField("HardcoreToggle", BindingFlags.Public | BindingFlags.Instance);
        _modsOnlyToggleField = _freeplayDeviceType.GetField("ModsOnly", BindingFlags.Public | BindingFlags.Instance);
        _startButtonField = _freeplayDeviceType.GetField("StartButton", BindingFlags.Public | BindingFlags.Instance);
        _currentSettingsField = _freeplayDeviceType.GetProperty("CurrentSettings", BindingFlags.Public | BindingFlags.Instance);
        _maxModuleField = _freeplayDeviceType.GetField("maxModules", BindingFlags.NonPublic | BindingFlags.Instance);
        _MAXSECONDSFIELD = _freeplayDeviceType.GetField("MAX_SECONDS_TO_SOLVE", BindingFlags.Public | BindingFlags.Static);

        _freeplaySettingsType = ReflectionHelper.FindType("Assets.Scripts.Settings.FreeplaySettings");
        _moduleCountField = _freeplaySettingsType.GetField("ModuleCount", BindingFlags.Public | BindingFlags.Instance);
        _timeField = _freeplaySettingsType.GetField("Time", BindingFlags.Public | BindingFlags.Instance);
        _isHardCoreField = _freeplaySettingsType.GetField("IsHardCore", BindingFlags.Public | BindingFlags.Instance);
        _hasNeedyField = _freeplaySettingsType.GetField("HasNeedy", BindingFlags.Public | BindingFlags.Instance);
        _onlyModsField = _freeplaySettingsType.GetField("OnlyMods", BindingFlags.Public | BindingFlags.Instance);

        _floatingHoldableType = ReflectionHelper.FindType("FloatingHoldable");
        if (_floatingHoldableType == null)
        {
            return;
        }
        _pickupTimeField = _floatingHoldableType.GetField("PickupTime", BindingFlags.Public | BindingFlags.Instance);
        _holdStateProperty = _floatingHoldableType.GetProperty("HoldState", BindingFlags.Public | BindingFlags.Instance);

        _selectableType = ReflectionHelper.FindType("Selectable");
        _handleSelectMethod = _selectableType.GetMethod("HandleSelect", BindingFlags.Public | BindingFlags.Instance);
        _onInteractEndedMethod = _selectableType.GetMethod("OnInteractEnded", BindingFlags.Public | BindingFlags.Instance);
        _childrenField = _selectableType.GetField("Children", BindingFlags.Public | BindingFlags.Instance);
        

        _selectableManagerType = ReflectionHelper.FindType("SelectableManager");
        if (_selectableManagerType == null)
        {
            return;
        }
        _selectMethod = _selectableManagerType.GetMethod("Select", BindingFlags.Public | BindingFlags.Instance);
        _handleInteractMethod = _selectableManagerType.GetMethod("HandleInteract", BindingFlags.Public | BindingFlags.Instance);
        _handleCancelMethod = _selectableManagerType.GetMethod("HandleCancel", BindingFlags.Public | BindingFlags.Instance);
        _setZSpinMethod = _selectableManagerType.GetMethod("SetZSpin", BindingFlags.Public | BindingFlags.Instance);
        _setControlsRotationMethod = _selectableManagerType.GetMethod("SetControlsRotation", BindingFlags.Public | BindingFlags.Instance);
        _getBaseHeldObjectTransformMethod = _selectableManagerType.GetMethod("GetBaseHeldObjectTransform", BindingFlags.Public | BindingFlags.Instance);
        _handleFaceSelectionMethod = _selectableManagerType.GetMethod("HandleFaceSelection", BindingFlags.Public | BindingFlags.Instance);

        _inputManagerType = ReflectionHelper.FindType("KTInputManager");
        if (_inputManagerType == null)
        {
            return;
        }
        _instanceProperty = _inputManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        _selectableManagerProperty = _inputManagerType.GetProperty("SelectableManager", BindingFlags.Public | BindingFlags.Instance);

        _inputManager = (MonoBehaviour)_instanceProperty.GetValue(null, null);
    }

    public FreeplayCommander(MonoBehaviour freeplayDevice)
    {
        FreeplayDevice = freeplayDevice;
        Selectable = (MonoBehaviour)FreeplayDevice.GetComponent(_selectableType);
        SelectableChildren = (MonoBehaviour[]) _childrenField.GetValue(Selectable);
        FloatingHoldable = (MonoBehaviour)FreeplayDevice.GetComponent(_floatingHoldableType);
        SelectableManager = (MonoBehaviour)_selectableManagerProperty.GetValue(_inputManager, null);
    }
    #endregion

    #region Interface Implementation

    public IEnumerator RespondToCommand(string userNickName, string message, ICommandResponseNotifier responseNotifier, IRCConnection connection)
    {
        IEnumerator respond = FreeplayRespondToCommand(userNickName, message, responseNotifier, connection);
        bool result;
        do
        {
            try
            {
                result = respond.MoveNext();
            }
            catch (Exception ex)
            {
                DebugHelper.LogException(ex);
                result = false;
            }
            if (result)
                yield return respond.Current;
        } while (result);
    }


    public IEnumerator FreeplayRespondToCommand(string userNickName, string message, ICommandResponseNotifier responseNotifier, IRCConnection connection)
    {
        message = message.ToLowerInvariant();
        int holdState = (int)_holdStateProperty.GetValue(FloatingHoldable, null);
        
        if (holdState == 0)
        {
            if (message.EqualsAny("drop","let go","put down"))
            {
                LetGoFreeplayDevice();
                yield break;
            }
        }
        else
        {
            IEnumerator holdCoroutine = HoldFreeplayDevice();
            while (holdCoroutine.MoveNext())
            {
                yield return holdCoroutine.Current;
            }
        }

        string changeHoursTo = string.Empty;
        string changeMinutesTo = string.Empty;
        string changeSecondsTo = string.Empty;
        string changeBombsTo = string.Empty;
        string changeModulesTo = string.Empty;
        bool startBomb = false;

        if (message.EqualsAny("needy on", "needy off"))
        {
            if (TwitchPlaySettings.data.EnableFreeplayNeedy || UserAccess.HasAccess(userNickName, AccessLevel.Admin, true))
            {
                SetNeedy(message.Equals("needy on"));
            }
            else
            {
                connection.SendMessage(TwitchPlaySettings.data.FreePlayNeedyDisabled, userNickName);
            }
        }
        else if (message.EqualsAny("hardcore on", "hardcore off"))
        {
            if (TwitchPlaySettings.data.EnableFreeplayHardcore || UserAccess.HasAccess(userNickName, AccessLevel.Admin, true))
            {
                SetHardcore(message.Equals("hardcore on"));
            }
            else
            {
                connection.SendMessage(TwitchPlaySettings.data.FreePlayHardcoreDisabled, userNickName);
            }
        }
        else if (message.EqualsAny("mods only on", "mods only off"))
        {
            if (TwitchPlaySettings.data.EnableFreeplayModsOnly || UserAccess.HasAccess(userNickName, AccessLevel.Admin, true))
            {
                SetModsOnly(message.Equals("mods only on"));
            }
            else
            {
                connection.SendMessage(TwitchPlaySettings.data.FreePlayModsOnlyDisabled, userNickName);
            }
        }
        else if (message.Equals("start"))
        {
            StartBomb();
        }
        else if (message.StartsWith("profile"))
        {
            string profile = message.Remove(0, 8).Trim();

            switch (profile)
            {
                case "single":
                case "solo":
                    changeBombsTo = "1";
                    changeHoursTo = "0";
                    changeMinutesTo = "20";
                    changeModulesTo = "11";
                    break;

                case "double":
                    changeBombsTo = "1";
                    changeHoursTo = "0";
                    changeMinutesTo = "40";
                    changeModulesTo = "23";
                    break;

                case "quadruple":
                case "quad":
                    changeBombsTo = "1";
                    changeHoursTo = "1";
                    changeMinutesTo = "20";
                    changeModulesTo = "47";
                    break;

                case "dual single":
                case "dual solo":
                    changeBombsTo = "2";
                    changeHoursTo = "0";
                    changeMinutesTo = "40";
                    changeModulesTo = "11";
                    break;

                case "dual double":
                    changeBombsTo = "2";
                    changeHoursTo = "1";
                    changeMinutesTo = "20";
                    changeModulesTo = "23";
                    break;

                case "dual quadruple":
                case "dual quad":
                    changeBombsTo = "2";
                    changeHoursTo = "2";
                    changeMinutesTo = "40";
                    changeModulesTo = "47";
                    break;
            }
        }
        else if (message.StartsWith("start"))
        {
            Match timerMatch = Regex.Match(message, "([0-9]):([0-9]{2}):([0-9]{2})");
            if (timerMatch.Success)
            {
                changeHoursTo = timerMatch.Groups[1].Value;
                changeMinutesTo = timerMatch.Groups[2].Value;
                changeSecondsTo = timerMatch.Groups[3].Value;
                message = message.Remove(timerMatch.Index, timerMatch.Length);
            }
            else
            {
                timerMatch = Regex.Match(message, "([0-9]+):([0-9]{2})");
                if (timerMatch.Success)
                {
                    changeMinutesTo = timerMatch.Groups[1].Value;
                    changeSecondsTo = timerMatch.Groups[2].Value;
                    message = message.Remove(timerMatch.Index, timerMatch.Length);
                }
            }


            Match modulesMatch = Regex.Match(message, "[0-9]+");

            while (modulesMatch.Success)
            {
                int count = 0;

                if (int.TryParse(modulesMatch.Value, out count))
                {
                    if (count <= 2)
                    {
                        changeBombsTo = modulesMatch.Value;
                    }
                    else
                    {
                        changeModulesTo = modulesMatch.Value;
                    }

                    DebugHelper.Log("[FreeplayCommander] Setting {1} to {0}", modulesMatch.Value,
                        count <= 2 ? "bombs" : "modules");
                }
                message = message.Remove(modulesMatch.Index, modulesMatch.Length);
                modulesMatch = Regex.Match(message, "[0-9]+");
            }

            startBomb = true;

            if (TwitchPlaySettings.data.EnableFreeplayHardcore || UserAccess.HasAccess(userNickName, AccessLevel.Admin, true))
            {
                SetHardcore(message.Contains("hardcore"));
            }
            else if (message.Contains("hardcore") != IsHardcore)
            {
                connection.SendMessage(TwitchPlaySettings.data.FreePlayHardcoreDisabled, userNickName);
                startBomb = false;
            }

            if (TwitchPlaySettings.data.EnableFreeplayNeedy || UserAccess.HasAccess(userNickName, AccessLevel.Admin, true))
            {
                SetNeedy(message.Contains("needy"));
            }
            else if (message.Contains("needy") != HasNeedy)
            {
                connection.SendMessage(TwitchPlaySettings.data.FreePlayNeedyDisabled, userNickName);
                startBomb = false;
            }

            if (TwitchPlaySettings.data.EnableFreeplayModsOnly || UserAccess.HasAccess(userNickName, AccessLevel.Admin, true))
            {
                if (message.Contains("vanilla"))
                {
                    SetModsOnly(false);
                }
                else if (message.Contains("mods"))
                {
                    SetModsOnly();
                }
            }
            else if (message.Contains("vanilla") || message.Contains("mods"))
            {
                connection.SendMessage(TwitchPlaySettings.data.FreePlayModsOnlyDisabled,userNickName);
                startBomb = false;
            }

            
        }
        else
        {
            Match timerMatch = Regex.Match(message, "^timer? ([0-9]+)(?::([0-9]{2}))(?::([0-9]{2}))$", RegexOptions.IgnoreCase);
            if (timerMatch.Success)
            {
                changeHoursTo = timerMatch.Groups[1].Value;
                changeMinutesTo = timerMatch.Groups[2].Value;
                changeSecondsTo = timerMatch.Groups[3].Value;
            }

            timerMatch = Regex.Match(message, "^timer? ([0-9]+)(?::([0-9]{2}))?$", RegexOptions.IgnoreCase);
            if (timerMatch.Success)
            {
                changeMinutesTo = timerMatch.Groups[1].Value;
                changeSecondsTo = timerMatch.Groups[2].Value;
            }

            Match bombsMatch = Regex.Match(message, "^bombs ([0-9]+)$", RegexOptions.IgnoreCase);
            if (bombsMatch.Success)
            {
                changeBombsTo = bombsMatch.Groups[1].Value;
            }

            Match modulesMatch = Regex.Match(message, "^modules ([0-9]+)$", RegexOptions.IgnoreCase);
            if (modulesMatch.Success)
            {
                changeModulesTo = modulesMatch.Groups[1].Value;
            }
        }

        if (changeMinutesTo != string.Empty)
        {
            IEnumerator setTimerCoroutine = SetBombTimer(changeHoursTo, changeMinutesTo, changeSecondsTo);
            while (setTimerCoroutine.MoveNext())
            {
                yield return setTimerCoroutine.Current;
            }
        }
        if (changeBombsTo != string.Empty)
        {
            IEnumerator setBombsCoroutine = SetBombCount(changeBombsTo);
            while (setBombsCoroutine.MoveNext())
            {
                yield return setBombsCoroutine.Current;
            }
        }
        if (changeModulesTo != string.Empty)
        {
            IEnumerator setModulesCoroutine = SetBombModules(changeModulesTo);
            while (setModulesCoroutine.MoveNext())
            {
                yield return setModulesCoroutine.Current;
            }
        }
        if (startBomb)
        {
            StartBomb();
        }

    }
    #endregion

    #region Helper Methods
    public IEnumerator SetBombTimer(string hours, string mins, string secs)
    {
        DebugHelper.Log("Time parsing section");
        int hoursInt = 0;
        if (!string.IsNullOrEmpty(hours) && !int.TryParse(hours, out hoursInt))
        {
            yield break;
        }

        int minutes = 0;
        if (!int.TryParse(mins, out minutes))
        {
            yield break;
        }

        int seconds = 0;
        if (!string.IsNullOrEmpty(secs) && 
            (!int.TryParse(secs, out seconds) || seconds >= 60) )
        {
            yield break;
        }

        int timeIndex = (hoursInt * 120) + (minutes * 2) + (seconds / 30);
        DebugHelper.Log("Freeplay time doubling section");
        //Double the available free play time. (The doubling stacks with the Multiple bombs module installed)
        float originalMaxTime = (float) _MAXSECONDSFIELD.GetValue(null);
        int maxModules = (int)_maxModuleField.GetValue(FreeplayDevice);
        int multiplier = MultipleBombs.Installed() ? (MultipleBombs.GetMaximumBombCount() * 2) - 1 : 1;
        float newMaxTime = 600f + ((maxModules - 1) * multiplier * 60);
        _MAXSECONDSFIELD.SetValue(null, newMaxTime);


        DebugHelper.Log("Freeplay settings reading section");
        object currentSettings = _currentSettingsField.GetValue(FreeplayDevice,null); DebugHelper.Log("1");
        float currentTime = (float)_timeField.GetValue(currentSettings); DebugHelper.Log("2");
        int currentTimeIndex = Mathf.FloorToInt(currentTime) / 30; DebugHelper.Log("3");
        MonoBehaviour button = timeIndex > currentTimeIndex ? (MonoBehaviour)_timeIncrementField.GetValue(FreeplayDevice) : (MonoBehaviour)_timeDecrementField.GetValue(FreeplayDevice); DebugHelper.Log("4");
        MonoBehaviour buttonSelectable = (MonoBehaviour)button.GetComponent(_selectableType); DebugHelper.Log("5");

        DebugHelper.Log("Freeplay time setting section");
        for (int hitCount = 0; hitCount < Mathf.Abs(timeIndex - currentTimeIndex); ++hitCount)
        {
            currentTime = (float)_timeField.GetValue(currentSettings);
            SelectObject(buttonSelectable);
            yield return new WaitForSeconds(0.01f);
            if (Mathf.FloorToInt(currentTime) == Mathf.FloorToInt((float) _timeField.GetValue(currentSettings)))
                break;
        }

        //Restore original max time, just in case Multiple bombs module was NOT installed, to avoid false detection.
        _MAXSECONDSFIELD.SetValue(null, originalMaxTime);
    }

    public IEnumerator SetBombCount(string bombs)
    {
        int bombCount = 0;
        if (!int.TryParse(bombs, out bombCount))
        {
            yield break;
        }

        if (!MultipleBombs.Installed())
        {
            yield break;
        }

        int currentBombCount = MultipleBombs.GetFreePlayBombCount();
        MonoBehaviour buttonSelectable = bombCount > currentBombCount ? SelectableChildren[3] : SelectableChildren[2];

        for (int hitCount = 0; hitCount < Mathf.Abs(bombCount - currentBombCount); ++hitCount)
        {
            int lastBombCount = MultipleBombs.GetFreePlayBombCount();
            SelectObject(buttonSelectable);
            yield return new WaitForSeconds(0.01f);
            if (lastBombCount == MultipleBombs.GetFreePlayBombCount())
                yield break;
        }
    }

    public IEnumerator SetBombModules(string mods)
    {
        int moduleCount = 0;
        if (!int.TryParse(mods, out moduleCount))
        {
            yield break;
        }

        object currentSettings = _currentSettingsField.GetValue(FreeplayDevice, null);
        int currentModuleCount = (int)_moduleCountField.GetValue(currentSettings);
        MonoBehaviour button = moduleCount > currentModuleCount ? (MonoBehaviour)_moduleCountIncrementField.GetValue(FreeplayDevice) : (MonoBehaviour)_moduleCountDecrementField.GetValue(FreeplayDevice);
        MonoBehaviour buttonSelectable = (MonoBehaviour)button.GetComponent(_selectableType);

        for (int hitCount = 0; hitCount < Mathf.Abs(moduleCount - currentModuleCount); ++hitCount)
        {
            int lastModuleCount = (int)_moduleCountField.GetValue(currentSettings);
            SelectObject(buttonSelectable);
            yield return new WaitForSeconds(0.01f);
            if (lastModuleCount == (int)_moduleCountField.GetValue(currentSettings))
                yield break;
        }
    }

    public void SetNeedy(bool on = true)
    {
        if (HasNeedy != on)
        {
            MonoBehaviour needyToggle = (MonoBehaviour)_needyToggleField.GetValue(FreeplayDevice);
            SelectObject( (MonoBehaviour)needyToggle.GetComponent(_selectableType) );
        }
    }

    public void SetHardcore(bool on = true)
    {
        if (IsHardcore != on)
        {
            MonoBehaviour hardcoreToggle = (MonoBehaviour)_hardcoreToggleField.GetValue(FreeplayDevice);
            SelectObject( (MonoBehaviour)hardcoreToggle.GetComponent(_selectableType) );
        }
    }

    public void SetModsOnly(bool on = true)
    {
        object currentSettings = _currentSettingsField.GetValue(FreeplayDevice, null);
        bool onlyMods = (bool)_onlyModsField.GetValue(currentSettings);
        if (onlyMods != on)
        {
            MonoBehaviour modsToggle = (MonoBehaviour)_modsOnlyToggleField.GetValue(FreeplayDevice);
            SelectObject( (MonoBehaviour)modsToggle.GetComponent(_selectableType) );
        }
    }

    public void StartBomb()
    {
        MonoBehaviour startButton = (MonoBehaviour)_startButtonField.GetValue(FreeplayDevice);
        SelectObject( (MonoBehaviour)startButton.GetComponent(_selectableType) );
    }

    public IEnumerator HoldFreeplayDevice()
    {
        int holdState = (int)_holdStateProperty.GetValue(FloatingHoldable, null);

        if (holdState != 0)
        {
            SelectObject(Selectable);

            float holdTime = (float)_pickupTimeField.GetValue(FloatingHoldable);
            IEnumerator forceRotationCoroutine = ForceHeldRotation(holdTime);
            while (forceRotationCoroutine.MoveNext())
            {
                yield return forceRotationCoroutine.Current;
            }
        }
    }

    public void LetGoFreeplayDevice()
    {
        int holdState = (int)_holdStateProperty.GetValue(FloatingHoldable, null);
        if (holdState == 0)
        {
            DeselectObject(Selectable);
        }
    }

    private void SelectObject(MonoBehaviour selectable)
    {
        _handleSelectMethod.Invoke(selectable, new object[] { true });
        _selectMethod.Invoke(SelectableManager, new object[] { selectable, true });
        _handleInteractMethod.Invoke(SelectableManager, null);
        _onInteractEndedMethod.Invoke(selectable, null);
    }

    private void DeselectObject(MonoBehaviour selectable)
    {
        _handleCancelMethod.Invoke(SelectableManager, null);
    }

    private IEnumerator ForceHeldRotation(float duration)
    {
        Transform baseTransform = (Transform)_getBaseHeldObjectTransformMethod.Invoke(SelectableManager, null);

        float initialTime = Time.time;
        while (Time.time - initialTime < duration)
        {
            Quaternion currentRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);

            _setZSpinMethod.Invoke(SelectableManager, new object[] { 0.0f });
            _setControlsRotationMethod.Invoke(SelectableManager, new object[] { baseTransform.rotation * currentRotation });
            _handleFaceSelectionMethod.Invoke(SelectableManager, null);
            yield return null;
        }

        _setZSpinMethod.Invoke(SelectableManager, new object[] { 0.0f });
        _setControlsRotationMethod.Invoke(SelectableManager, new object[] { baseTransform.rotation * Quaternion.Euler(0.0f, 0.0f, 0.0f) });
        _handleFaceSelectionMethod.Invoke(SelectableManager, null);
    }
    #endregion

    #region Readonly Fields
    public readonly MonoBehaviour FreeplayDevice = null;
    public readonly MonoBehaviour Selectable = null;
    public readonly MonoBehaviour[] SelectableChildren = null;
    public readonly MonoBehaviour FloatingHoldable = null;
    private readonly MonoBehaviour SelectableManager = null;
    public bool HasNeedy { get { object currentSettings = _currentSettingsField.GetValue(FreeplayDevice, null); return (bool)_hasNeedyField.GetValue(currentSettings); }}
    public bool IsHardcore { get { object currentSettings = _currentSettingsField.GetValue(FreeplayDevice, null); return (bool)_isHardCoreField.GetValue(currentSettings); }}
    #endregion

    #region Private Static Fields
    private static Type _freeplayDeviceType = null;
    private static FieldInfo _moduleCountIncrementField = null;
    private static FieldInfo _moduleCountDecrementField = null;
    private static FieldInfo _timeIncrementField = null;
    private static FieldInfo _timeDecrementField = null;
    private static FieldInfo _needyToggleField = null;
    private static FieldInfo _hardcoreToggleField = null;
    private static FieldInfo _modsOnlyToggleField = null;
    private static FieldInfo _startButtonField = null;
    private static FieldInfo _maxModuleField = null;
    private static FieldInfo _MAXSECONDSFIELD = null;
    private static PropertyInfo _currentSettingsField = null;

    private static Type _freeplaySettingsType = null;
    private static FieldInfo _moduleCountField = null;
    private static FieldInfo _timeField = null;
    private static FieldInfo _isHardCoreField = null;
    private static FieldInfo _hasNeedyField = null;
    private static FieldInfo _onlyModsField = null;

    private static Type _floatingHoldableType = null;
    private static FieldInfo _pickupTimeField = null;
    private static PropertyInfo _holdStateProperty = null;

    private static Type _selectableType = null;
    private static MethodInfo _handleSelectMethod = null;
    private static MethodInfo _onInteractEndedMethod = null;
    private static FieldInfo _childrenField = null;

    private static Type _selectableManagerType = null;
    private static MethodInfo _selectMethod = null;
    private static MethodInfo _handleInteractMethod = null;
    private static MethodInfo _handleCancelMethod = null;
    private static MethodInfo _setZSpinMethod = null;
    private static MethodInfo _setControlsRotationMethod = null;
    private static MethodInfo _getBaseHeldObjectTransformMethod = null;
    private static MethodInfo _handleFaceSelectionMethod = null;

    private static Type _inputManagerType = null;
    private static PropertyInfo _instanceProperty = null;
    private static PropertyInfo _selectableManagerProperty = null;

    private static MonoBehaviour _inputManager = null;
    #endregion
}

