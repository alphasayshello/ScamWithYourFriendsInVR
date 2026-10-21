using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestNativePaintPrint(object paint, VRPanelSurface surface)
        {
            Type type = Find("JwPaintPrinter");
            var printer = AccessTools.Property(type, "Instance").GetValue(null) as Component;
            object game = Instance("ScammerGameManager");
            int day = Convert.ToInt32(Member(game, "CurrentDay"));
            var dayProperty = AccessTools.Property(game.GetType(), "NetworkcurrentDay");
            Require(printer != null && !(bool)Member(printer, "Busy") && Member(printer, "Output") == null,
                "Native printer fixture requires an idle printer without a pending user drawing");
            Require(day < 5, "Native printer fixture needs a locked starting day");
            var printButton = Member(paint, "m_printButton") as Button;
            var canvas = Member(Member(paint, "m_canvas"), "Texture") as Texture2D;
            Require(printButton != null && !printButton.enabledInHierarchy && !(bool)AccessTools.Property(type, "CanPrint").GetValue(null),
                "Native print control ignored its day-five unlock rule");
            Component output = null;
            try
            {
                // Arrange only this private host's synchronized day state. Do not
                // start/end a day, award progress or write a campaign checkpoint.
                dayProperty.SetValue(game, 5);
                yield return null;
                Require(printButton.enabledInHierarchy, "Native paint print button did not unlock on day five");
                byte[] expected = (byte[])AccessTools.Method(Find("JwPrintImage"), "Capture").Invoke(null, new object[] { canvas });
                var click = ClickAppControl(surface, printButton);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                float deadline = Time.realtimeSinceStartup + 8;
                while (Member(printer, "Output") is not Component)
                { Require(Time.realtimeSinceStartup < deadline, "Controller print did not produce native networked artwork"); yield return null; }
                output = Member(printer, "Output") as Component;
                deadline = Time.realtimeSinceStartup + 5;
                while (Member(output, "imageTexture") is not Texture2D && Time.realtimeSinceStartup < deadline) yield return null;
                Require(output.GetComponent<NetworkIdentity>().netId != 0 && expected != null
                    && (Member(output, "ImageBytes") as byte[]).SequenceEqual(expected)
                    && Member(output, "imageTexture") is Texture2D decoded && decoded.width >= 64,
                    "Native printed drawing did not synchronize/decode the controller artwork image; netId=" + output.GetComponent<NetworkIdentity>().netId
                    + "; expectedBytes=" + expected?.Length + "; actualBytes=" + (Member(output, "ImageBytes") as byte[])?.Length
                    + "; texture=" + (Member(output, "imageTexture") is Texture2D));
                Require((bool)Member(printer, "Busy") && !printButton.enabledInHierarchy
                    && !(bool)AccessTools.Method(type, "Print").Invoke(printer, new object[] { canvas }),
                    "Occupied native printer accepted another drawing");
                Logger.LogInfo("PAINT PRINT PASS: day-five controller print reaches native authority, spawns matching networked image and rejects a second print while occupied");
                var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
                Require(Member(interactor, "CurrentHeldItem") == null, "Printer collection fixture requires empty hands");
                AccessTools.Method(output.GetType(), "CmdInteract").Invoke(output, new object[] { interactor });
                deadline = Time.realtimeSinceStartup + 5;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), output))
                { Require(Time.realtimeSinceStartup < deadline, "Native pickup did not collect the printed drawing"); yield return null; }
                Require(!(bool)Member(printer, "Busy") && Member(printer, "Output") == null,
                    "Native printed artwork pickup did not release printer availability");
                AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                Logger.LogInfo("PAINT COLLECT PASS: native artwork pickup clears printer output/busy state and restores printing availability");
            }
            finally
            {
                if (output == null) output = Member(printer, "Output") as Component;
                if (output != null)
                {
                    var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), output))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    AccessTools.Method(type, "OnOutputTaken").Invoke(printer, new object[] { output });
                    NetworkServer.Destroy(output.gameObject);
                }
                AccessTools.Field(type, "pendingImage").SetValue(printer, null);
                AccessTools.Field(type, "requester").SetValue(printer, null);
                AccessTools.Property(type, "Networkbusy").SetValue(printer, false);
                dayProperty.SetValue(game, day);
            }
        }
    }
}
