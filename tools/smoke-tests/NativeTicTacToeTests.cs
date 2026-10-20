using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        // Called inside the owned lobby's temporary app-access scope. Never buys
        // a licence or reuses the player's existing solo board.
        private IEnumerator TestNativeTicTacToe(object os, VRPanelSurface surface)
        {
            var apps = Member(os, "m_appsById") as IDictionary;
            string id = apps.Keys.Cast<string>().Single(key => Member(Member(apps[key], "Definition"), "contentType") as string == "ticTacToe");
            object app = apps[id];
            string originalState = Member(app, "State")?.ToString();
            object player = AccessTools.Property(Find("KolkataOsPlayer"), "LocalPlayerInstance").GetValue(null);
            string Phase() => Member(Member(player, "TicTacToeState"), "phase")?.ToString();
            Require(player != null && Phase() == "Idle", "Tic-Tac-Toe fixture requires an idle local player");
            AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
            var field = AccessTools.Field(os.GetType(), "m_ticTacToeApp");
            object original = field.GetValue(os);
            var content = Member(app, "WindowContent") as VisualElement;
            Require(original != null && content != null, "Native Tic-Tac-Toe owner/content missing");
            var children = content.Children().ToArray();
            object fixture = null;
            try
            {
                fixture = Activator.CreateInstance(original.GetType(), true);
                field.SetValue(os, fixture);
                AccessTools.Method(fixture.GetType(), "BuildContent").Invoke(fixture, new[] { app });
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                yield return null; yield return null;
                object board = Member(fixture, "solo");
                var cells = Member(fixture, "cells") as Button[];
                Require(board != null && cells?.Length == 9 && cells.All(cell => cell != null && cell.enabledInHierarchy),
                    "Native solo board did not start with nine playable cells");
                int moves = 0;
                while (Member(board, "Result")?.ToString() == "Playing")
                {
                    int beforeX = Convert.ToInt32(Member(board, "X")), beforeO = Convert.ToInt32(Member(board, "O"));
                    int cell = Array.FindIndex(cells, entry => entry.enabledInHierarchy);
                    Require(cell >= 0 && moves < 5, "Native solo board stalled before its result");
                    var click = ClickAppControl(surface, cells[cell]);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    int afterX = Convert.ToInt32(Member(board, "X")), afterO = Convert.ToInt32(Member(board, "O"));
                    Require(afterX == (beforeX | (1 << cell)) && cells[cell].text == "X" && !cells[cell].enabledInHierarchy,
                        "Controller move did not mark exactly its native square");
                    bool playing = Member(board, "Result")?.ToString() == "Playing";
                    Require(afterO != beforeO || !playing, "Native solo opponent did not reply");
                    for (int index = 0; index < cells.Length; index++)
                    {
                        int bit = 1 << index;
                        string expected = (afterX & bit) != 0 ? "X" : (afterO & bit) != 0 ? "O" : "";
                        Require(cells[index].text == expected && cells[index].enabledInHierarchy == (playing && expected.Length == 0),
                            "Native board graphics/input disagree with its game state");
                    }
                    moves++;
                }
                Require(!string.IsNullOrWhiteSpace((Member(fixture, "status") as Label)?.text)
                    && cells.All(cell => !cell.enabledInHierarchy), "Completed solo round did not display a result and lock its board");
                Logger.LogInfo("TIC TAC TOE PASS: controller plays a complete native solo round, receives opponent replies and locks the finished board; moves=" + moves);
                var reset = ClickAppControl(surface, Member(fixture, "action") as Button);
                try { while (reset.MoveNext()) yield return reset.Current; }
                finally { (reset as IDisposable)?.Dispose(); }
                Require(Convert.ToInt32(Member(board, "X")) == 0 && Convert.ToInt32(Member(board, "O")) == 0
                    && cells.All(cell => cell.text == "" && cell.enabledInHierarchy), "Controller New Game did not clear/re-enable the native board");
                Logger.LogInfo("TIC TAC TOE PASS: controller New Game clears the native round and restores all nine playable squares");

                var mode = ClickAppControl(surface, Member(fixture, "multiplayerMode") as Button);
                try { while (mode.MoveNext()) yield return mode.Current; }
                finally { (mode as IDisposable)?.Dispose(); }
                Require(Member(fixture, "multiplayer") is bool multiplayer && multiplayer && cells.All(cell => !cell.enabledInHierarchy),
                    "Controller multiplayer mode did not select the native network view");
                var search = ClickAppControl(surface, Member(fixture, "action") as Button);
                try { while (search.MoveNext()) yield return search.Current; }
                finally { (search as IDisposable)?.Dispose(); }
                float deadline = Time.realtimeSinceStartup + 5;
                while (Phase() == "Idle")
                { Require(Time.realtimeSinceStartup < deadline, "Controller Find Coworker did not receive a native server response"); yield return null; }
                Require(Phase() == "Waiting", "Owned native Tic-Tac-Toe queue did not enter Waiting: " + Phase());
                search = ClickAppControl(surface, Member(fixture, "action") as Button);
                try { while (search.MoveNext()) yield return search.Current; }
                finally { (search as IDisposable)?.Dispose(); }
                deadline = Time.realtimeSinceStartup + 5;
                while (Phase() != "Idle")
                { Require(Time.realtimeSinceStartup < deadline, "Controller Cancel Search did not leave the native queue"); yield return null; }
                Logger.LogInfo("TIC TAC TOE PASS: controller multiplayer search reaches native server Waiting state and cancellation returns to Idle; matched games/rematches not covered");
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel(); holdInjectedTrigger = false;
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                (fixture as IDisposable)?.Dispose();
                field.SetValue(os, original);
                content.Clear(); foreach (var child in children) content.Add(child);
                if (originalState != "Closed") AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                if (originalState == "OpenMinimized") AccessTools.Method(os.GetType(), "MinimizeApp").Invoke(os, new[] { app });
            }
        }
    }
}
