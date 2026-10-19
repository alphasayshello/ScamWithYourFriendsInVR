using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestNativePhone(object os, VRPanelSurface surface)
        {
            object app = (Member(os, "m_appsById") as IDictionary)?["phone"];
            Require(app != null && Member(os, "m_boundVictimCall") == null, "Phone fixture requires an idle private workstation");
            string originalState = Member(app, "State")?.ToString();
            float originalVolume = Convert.ToSingle(Member(os, "PhoneSpeakerVolume"));
            bool hadVolumePref = PlayerPrefs.HasKey("ScammerWorkspace.SpeakerVolume");
            float originalVolumePref = PlayerPrefs.GetFloat("ScammerWorkspace.SpeakerVolume");
            bool originalMute = (bool)Member(os, "m_phoneMicrophoneMuted");
            object builder = Member(os, "m_contentBuilder");
            object beforeMute = Member(builder, "m_phoneVolumeBeforeMute");
            Type languageManager = Find("LanguageManager");
            string originalSelection = AccessTools.Property(languageManager, "SelectedCode").GetValue(null) as string;
            var languagePrefs = new Dictionary<string, string>();
            foreach (string key in new[] { "LanguageManager.LanguageCode", "Kolkata.CallLanguage" })
                languagePrefs[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            var manager = VRManager.Instance;
            var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
            var probe = new Harmony("com.swyf.vr.phonefixture");
            void Set(string key, bool value) => manager.Input.GetType().GetProperty(key).SetValue(manager.Input, value);
            float Volume() => Convert.ToSingle(Member(os, "PhoneSpeakerVolume"));
            try
            {
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "phone" });
                AccessTools.Method(os.GetType(), "SetPhoneSpeakerVolume").Invoke(os, new object[] { .65f });
                yield return null; yield return null;
                var content = Member(app, "WindowContent") as VisualElement;
                var languageButton = content?.Q<Button>("phone-language-button");
                var popup = content?.Q<VisualElement>("phone-language-popup");
                Require(languageButton != null && popup != null, "Native phone language controls missing");
                Type languageType = Find("Kolkata.Speech.CallLanguage");
                Require(languageType != null, "Native phone language catalog missing");
                object current = AccessTools.Property(languageType, "Current").GetValue(null);
                string originalLanguage = Member(current, "Code") as string;
                var click = ClickAppControl(surface, languageButton);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(popup.resolvedStyle.display != DisplayStyle.None, "Controller did not open the native phone language popup");
                var selected = popup.Query<Button>(className: "kolkata-phone-language-option").ToList().SingleOrDefault(b => b.userData as string == originalLanguage);
                Require(selected != null, "Current native phone language option missing");
                // Preserve the user's language; scrolling/translation acceptance is separate.
                popup.Q<ScrollView>().ScrollTo(selected); yield return null;
                click = ClickAppControl(surface, selected);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(AccessTools.Property(languageType, "Current").GetValue(null), "Code") as string == originalLanguage
                    && popup.resolvedStyle.display == DisplayStyle.None, "Controller language selection did not close the popup or changed the chosen language");
                Logger.LogInfo("PHONE PASS: controller opens the native language popup and selects the current language without changing it");
                var volumeButton = content.Q<Button>(className: "kolkata-phone-volume-button");
                click = ClickAppControl(surface, volumeButton);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Volume() < .001f, "Controller speaker mute did not reach native audio volume");
                click = ClickAppControl(surface, volumeButton);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Mathf.Abs(Volume() - .65f) < .001f, "Controller speaker unmute did not restore its native volume");
                Logger.LogInfo("PHONE PASS: controller caller-audio mute/unmute updates native speaker volume and restores the previous level");

                probe.Patch(tick, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AimAppFixture)));
                appFixtureSurface = surface; appFixturePoint = volumeButton.worldBound.center; appFixtureAim = true;
                tick.Invoke(manager.Pointer, null); yield return null; yield return null;
                var slider = content.Q<Slider>(className: "kolkata-phone-volume-slider");
                var dragger = slider?.Q<VisualElement>(className: "unity-base-slider__dragger");
                var track = slider?.Q<VisualElement>(className: "unity-base-slider__tracker");
                Require(dragger?.panel != null && track != null && track.worldBound.width > 5, "Native speaker volume slider did not lay out");
                appFixturePoint = dragger.worldBound.center;
                holdInjectedTrigger = true; Set("Trigger", true); Set("TriggerDown", true); tick.Invoke(manager.Pointer, null); Set("TriggerDown", false);
                var recipient = Member(manager.Pointer, "pressedElement") as VisualElement;
                Require(Member(manager.Pointer, "toolkitDown") is bool held && held && recipient != null
                    && ReferenceEquals(Member(manager.Pointer, "pressedPanel"), surface) && (recipient == slider || slider.Contains(recipient)),
                    "Controller did not begin a drag on the native speaker slider");
                appFixturePoint = new Vector2(track.worldBound.xMin + track.worldBound.width * .25f, track.worldBound.center.y);
                yield return null; yield return new WaitForEndOfFrame(); tick.Invoke(manager.Pointer, null);
                holdInjectedTrigger = false; Set("Trigger", false); Set("TriggerUp", true); tick.Invoke(manager.Pointer, null); Set("TriggerUp", false);
                Require(Volume() > .1f && Volume() < .5f && Mathf.Abs(slider.value - Volume()) < .001f,
                    "Controller slider drag did not update the native caller-audio level");
                Logger.LogInfo("PHONE PASS: controller drags the native caller volume slider and updates its actual audio setting");
                appFixtureAim = false;
                if (Microphone.devices.Length > 0)
                {
                    var microphone = content.Q<Button>(className: "kolkata-phone-microphone-button");
                    click = ClickAppControl(surface, microphone);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    Require((bool)Member(os, "m_phoneMicrophoneMuted") != originalMute, "Controller microphone button did not change native phone mute");
                    click = ClickAppControl(surface, microphone);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    Require((bool)Member(os, "m_phoneMicrophoneMuted") == originalMute, "Controller microphone button did not restore native phone mute");
                    Logger.LogInfo("PHONE PASS: controller microphone mute/unmute reaches native phone state with an available PC microphone");
                }
                else Logger.LogInfo("PHONE INFO: microphone control was not tested because the native game detected no recording device");
            }
            finally
            {
                appFixtureAim = false; appFixtureSurface = null; holdInjectedTrigger = false;
                Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false); manager.Pointer.Cancel(); probe.UnpatchSelf();
                AccessTools.Method(os.GetType(), "SetPhoneSpeakerVolume").Invoke(os, new object[] { originalVolume });
                AccessTools.Method(os.GetType(), "SetPhoneMicrophoneMuted").Invoke(os, new object[] { originalMute });
                AccessTools.Field(builder.GetType(), "m_phoneVolumeBeforeMute").SetValue(builder, beforeMute);
                AccessTools.Method(languageManager, "SelectLanguage").Invoke(null, new object[] { originalSelection });
                foreach (var pref in languagePrefs)
                    if (pref.Value != null) PlayerPrefs.SetString(pref.Key, pref.Value); else PlayerPrefs.DeleteKey(pref.Key);
                if (hadVolumePref) PlayerPrefs.SetFloat("ScammerWorkspace.SpeakerVolume", originalVolumePref);
                else PlayerPrefs.DeleteKey("ScammerWorkspace.SpeakerVolume");
                PlayerPrefs.Save();
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                if (originalState != "Closed") AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "phone" });
                if (originalState == "OpenMinimized") AccessTools.Method(os.GetType(), "MinimizeApp").Invoke(os, new[] { app });
            }
            Require(Mathf.Abs(Volume() - originalVolume) < .001f && (bool)Member(os, "m_phoneMicrophoneMuted") == originalMute,
                "Phone fixture did not restore native audio settings");
            Require(PlayerPrefs.HasKey("ScammerWorkspace.SpeakerVolume") == hadVolumePref
                && (!hadVolumePref || PlayerPrefs.GetFloat("ScammerWorkspace.SpeakerVolume") == originalVolumePref),
                "Phone fixture did not restore saved speaker preferences");
            Require(AccessTools.Property(languageManager, "SelectedCode").GetValue(null) as string == originalSelection,
                "Phone fixture did not restore the original language selection");
            foreach (var pref in languagePrefs)
                Require(PlayerPrefs.HasKey(pref.Key) == (pref.Value != null) && (pref.Value == null || PlayerPrefs.GetString(pref.Key) == pref.Value),
                    "Phone fixture did not restore a saved language preference");
            Logger.LogInfo("PHONE PASS: native audio state and saved volume/language preferences restored, including absent keys and automatic language selection");
        }
    }
}
