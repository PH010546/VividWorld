#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Persistence;

namespace VividWorld.Presentation
{
    /// <summary>
    /// SNAP1／SNAP3／SNAP4：玩家自己管快照（熱鍵叫出來）。
    ///
    /// 熱鍵按下去分兩段：
    /// <list type="number">
    ///   <item>有快照的存檔已經被刪掉 ⇒ 先問一句要不要把那幾份清掉，兩個鈕是「清除無用快照」與「看完整清單…」</item>
    ///   <item>沒有那種快照（或他選了看清單）⇒ 開多選清單，一張一張勾</item>
    /// </list>
    ///
    /// 用原生的視窗（多選 **D-68**、是／否 **D-69**），不做 Gauntlet 介面；存檔清單來自 `MBSaveLoad`（帳本 **D-66**）。
    /// **這支只准碰 <c>_snapshots\</c> 底下的東西**：列出來、玩家勾、刪掉。
    /// 現場資料夾一個位元組都不動；**一鍵清除只清「存檔已經刪掉」的那些**，
    /// 存檔還在的一律要玩家自己勾、而且勾了還要再確認一次（SNAP3）。
    /// </summary>
    internal static class SnapshotManagerScreen
    {
        private static bool _open;

        internal static bool IsOpen => _open;

        internal static void Show(string campaignId, VividWorldConfig config)
        {
            if (_open) return;

            try
            {
                string campaignRoot = VividWorldPaths.CampaignDirectory(
                    string.IsNullOrEmpty(campaignId) ? "legacy" : campaignId);

                var inv = RumorSnapshotStore.Inventory(campaignRoot);
                ModLog.Info(SnapshotManagerFormatter.FormatOpened(inv));

                if (!inv.FolderExists || inv.Slots.Count == 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage(Text("VividWorld_Snapshots_None",
                        "No snapshots yet - one is taken every time you save.")));
                    return;
                }

                // 存檔清單（帳本 D-66）。問不到就當作「不知道」，一律標成還在：
                // 寧可讓玩家多想一下，也不要把還在的存檔標成不在而害他刪掉。
                HashSet<string>? saveNames = null;
                try
                {
                    var names = MBSaveLoad.GetSaveFileNames();
                    if (names != null) saveNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    ModLog.Error("Snapshot manager: could not read the save file list", ex);
                }

                // SNAP4：存檔已經被刪掉的那幾份，先問要不要一口氣清掉。
                // 存檔還在的一份都不在這裡面——那種要他自己勾，而且勾了還會再問一次（SNAP3）。
                var gone = inv.Slots.Where(slot => !SaveExists(slot, saveNames)).ToList();
                if (gone.Count > 0)
                {
                    long goneBytes = gone.Sum(slot => slot.Bytes);
                    ModLog.Info(SnapshotManagerFormatter.FormatSweepOffer(
                        gone.Count, goneBytes, inv.Slots.Count - gone.Count));
                    ModLog.Flush();
                    OfferSweep(campaignRoot, inv, saveNames, gone, goneBytes);
                    return;
                }

                ModLog.Info(SnapshotManagerFormatter.FormatNothingToSweep(inv.Slots.Count));
                ShowList(campaignRoot, inv, saveNames);
            }
            catch (Exception ex)
            {
                _open = false;
                ModLog.Error("Snapshot manager failed to open", ex);
            }
        }

        /// <summary>那個存檔還在不在。問不到存檔清單時一律當成「還在」——寧可少刪，不要誤刪。</summary>
        private static bool SaveExists(SnapshotSlot slot, HashSet<string>? saveNames)
            => saveNames == null || saveNames.Contains(slot.SaveName);

        /// <summary>
        /// SNAP4 第一段：「有 N 份快照的存檔已經刪掉了，要現在清掉嗎？」
        /// 「清除無用快照」＝把那 N 份刪掉就結束；「看完整清單…」＝退回 SNAP1 的多選清單。
        /// </summary>
        private static void OfferSweep(string campaignRoot, SnapshotInventory inv,
            HashSet<string>? saveNames, List<SnapshotSlot> gone, long goneBytes)
        {
            // 「另外 N 份不會動到」那句刻意不寫 ⇒ 兩種情況同一句話，一個字串鍵就夠。
            var text = new TextObject("{=VividWorld_Snapshots_SweepText}{COUNT} of your snapshots belong to save games you have already deleted, and they are taking up {SIZE}. Clear them out?");
            text.SetTextVariable("COUNT", gone.Count);
            text.SetTextVariable("SIZE", SnapshotManagerFormatter.FormatSize(goneBytes));

            var tokens = gone.Select(slot => slot.Token).ToList();

            _open = true;
            try
            {
                InformationManager.ShowInquiry(
                    new InquiryData(
                        Text("VividWorld_Snapshots_SweepTitle", "Clear out the snapshots you no longer need"),
                        text.ToString(),
                        true,                       // 清除
                        true,                       // 看清單
                        Text("VividWorld_Snapshots_SweepYes", "Clear them out"),
                        Text("VividWorld_Snapshots_SweepNo", "Show the full list..."),
                        () => Sweep(campaignRoot, tokens),
                        () =>
                        {
                            ModLog.Info(SnapshotManagerFormatter.FormatSweepDeclined());
                            ShowList(campaignRoot, inv, saveNames);
                        },
                        string.Empty,
                        0f,
                        null,
                        null,
                        null),
                    false,
                    false);
            }
            catch (Exception ex)
            {
                _open = false;
                ModLog.Error("Snapshot manager failed to offer the sweep", ex);
                ModLog.Flush();
            }
        }

        /// <summary>SNAP4：把「存檔已經刪掉」的那幾份一次刪光。這些不必再確認——它們的存檔早就不在了。</summary>
        private static void Sweep(string campaignRoot, List<string> tokens)
        {
            ModLog.Info(SnapshotManagerFormatter.FormatSweepAccepted(tokens.Count));
            Delete(campaignRoot, tokens, 0);
        }

        /// <summary>SNAP1 的多選清單：一張一張勾。</summary>
        private static void ShowList(string campaignRoot, SnapshotInventory inv, HashSet<string>? saveNames)
        {
            try
            {
                string existsLabel = Text("VividWorld_Snapshots_SaveExists", "save exists");
                string goneLabel = Text("VividWorld_Snapshots_SaveGone", "save deleted");
                string missingFolder = Text("VividWorld_Snapshots_FolderMissing", "folder missing");

                // SNAP3：存檔還在的那幾張要先問過才刪 ⇒ 這裡就把 token 記下來，
                // 確認的回呼拿得到（回呼只收得到 InquiryElement，問不到存檔清單）。
                var savesStillThere = new HashSet<string>(StringComparer.Ordinal);

                var elements = new List<InquiryElement>();
                foreach (var slot in inv.Slots)
                {
                    bool saveExists = SaveExists(slot, saveNames);
                    if (saveExists) savesStillThere.Add(slot.Token);
                    var fileCount = new TextObject("{=VividWorld_Snapshots_FileCount}{COUNT} file(s)");
                    fileCount.SetTextVariable("COUNT", slot.Files);

                    elements.Add(new InquiryElement(
                        slot.Token,
                        SnapshotManagerFormatter.FormatRow(slot, saveExists, existsLabel, goneLabel),
                        null,
                        true,
                        SnapshotManagerFormatter.FormatHint(slot, missingFolder, fileCount.ToString())));
                }

                var description = new TextObject("{=VividWorld_Snapshots_Desc}A snapshot is what Vivid World uses to put the rumours back the way they were when you saved. Delete a snapshot while its save game is still there and Vivid World will start behaving oddly with that save. It is best to delete a snapshot only once its save game is gone. You have {COUNT}, taking up {SIZE}.");
                description.SetTextVariable("COUNT", inv.Slots.Count);
                description.SetTextVariable("SIZE", SnapshotManagerFormatter.FormatSize(inv.TotalBytes));

                _open = true;
                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        Text("VividWorld_Snapshots_Title", "Vivid World Snapshot Manager"),
                        description.ToString(),
                        elements,
                        true,                       // 右上角的離開鍵
                        0,                          // 最少可以一張都不勾
                        elements.Count,             // 最多全選
                        Text("VividWorld_Snapshots_Delete", "Delete selected"),
                        Text("VividWorld_Snapshots_Cancel", "Cancel"),
                        selected => OnConfirm(campaignRoot, selected, savesStillThere),
                        _ => OnCancel(),
                        string.Empty,
                        elements.Count > 8),        // 超過八張才需要搜尋框
                    false,
                    false);
            }
            catch (Exception ex)
            {
                _open = false;
                ModLog.Error("Snapshot manager failed to open the list", ex);
                ModLog.Flush();
            }
        }

        private static void OnConfirm(string campaignRoot, List<InquiryElement>? selected, HashSet<string> savesStillThere)
        {
            _open = false;
            try
            {
                var tokens = (selected ?? new List<InquiryElement>())
                    .Select(e => e?.Identifier as string)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(t => t!)
                    .ToList();

                if (tokens.Count == 0)
                {
                    ModLog.Info(SnapshotManagerFormatter.FormatNothingSelected());
                    ModLog.Flush();
                    return;
                }

                // SNAP3：勾到的裡面只要有一張的存檔還在，就先問一句再刪。
                // 那種快照刪掉之後，那個存檔再讀回來時傳聞就回不到當時的樣子
                // （`Restore` 會回 SnapshotMissing，現場資料原封不動 ⇒ 沿用現在的傳聞）。
                int guarded = tokens.Count(t => savesStillThere.Contains(t));
                if (guarded > 0)
                {
                    ModLog.Info(SnapshotManagerFormatter.FormatGuardPrompt(guarded, tokens.Count));
                    ModLog.Flush();
                    AskBeforeDeleting(campaignRoot, tokens, guarded);
                    return;
                }

                Delete(campaignRoot, tokens, 0);
            }
            catch (Exception ex)
            {
                ModLog.Error("Snapshot manager failed while deleting", ex);
                ModLog.Flush();
            }
        }

        /// <summary>
        /// SNAP3：存檔還在的那幾張，刪之前跳一個「是／否」（帳本 **D-69**）。
        /// 按取消就一張都不刪——包含那些存檔已經不在的，因為他看到的是整批，不是那幾張。
        ///
        /// 這段期間 <see cref="IsOpen"/> 維持 true：確認視窗還開著時再按一次熱鍵，
        /// 不該在它上面又疊一張清單。開不起來就立刻放掉，不然熱鍵會永遠失效。
        ///
        /// **`prioritize` 一定要傳 false**（帳本 **D-70**）：這支是從多選清單的「確定」回呼裡呼叫的，
        /// 那時清單還在畫面上，而回呼一回去清單自己就會關。傳 true 會讓引擎當場先關掉清單、
        /// 把確認視窗頂上來，緊接著清單那一關又把它收走（`CloseQuery` 收視窗時不回呼任何一邊）
        /// ⇒ 玩家什麼都沒看到，清單原地重開，一份也沒刪。
        /// 傳 false 是排進佇列，清單關掉時引擎自己把它叫出來——這正是我們要的順序。
        /// </summary>
        private static void AskBeforeDeleting(string campaignRoot, List<string> tokens, int guarded)
        {
            _open = true;
            var text = new TextObject("{=VividWorld_Snapshots_ConfirmText}{COUNT} of the snapshots you ticked still have their save game. Delete them and those saves can no longer rewind their rumours. Delete anyway?");
            text.SetTextVariable("COUNT", guarded);

            try
            {
                InformationManager.ShowInquiry(
                    new InquiryData(
                        Text("VividWorld_Snapshots_ConfirmTitle", "Those saves are still there"),
                        text.ToString(),
                        true,                       // 是
                        true,                       // 否
                        Text("VividWorld_Snapshots_ConfirmYes", "Delete anyway"),
                        Text("VividWorld_Snapshots_ConfirmNo", "Cancel"),
                        () => Delete(campaignRoot, tokens, guarded),
                        () =>
                        {
                            _open = false;
                            ModLog.Info(SnapshotManagerFormatter.FormatGuardDeclined());
                            ModLog.Flush();
                        },
                        string.Empty,
                        0f,
                        null,
                        null,
                        null),
                    false,
                    false);                     // 不插隊：排在多選清單後面，清單關掉時才跳（D-70）
            }
            catch (Exception ex)
            {
                _open = false;
                ModLog.Error("Snapshot manager failed to ask before deleting", ex);
                ModLog.Flush();
            }
        }

        /// <summary>真的刪。<paramref name="guarded"/> 大於 0 表示他在確認視窗按過「還是刪掉」。</summary>
        private static void Delete(string campaignRoot, List<string> tokens, int guarded)
        {
            _open = false;
            try
            {
                if (guarded > 0)
                {
                    ModLog.Info(SnapshotManagerFormatter.FormatGuardConfirmed(guarded));
                }

                var result = RumorSnapshotStore.Delete(campaignRoot, tokens);
                foreach (string line in SnapshotManagerFormatter.FormatDeleted(result, tokens.Count))
                {
                    ModLog.Info(line);
                }

                var message = new TextObject("{=VividWorld_Snapshots_Deleted}Deleted {COUNT} snapshot(s), freeing {SIZE}.");
                message.SetTextVariable("COUNT", result.Deleted.Count);
                message.SetTextVariable("SIZE", SnapshotManagerFormatter.FormatSize(result.BytesFreed));
                InformationManager.DisplayMessage(new InformationMessage(message.ToString()));

                if (result.Failed.Count > 0)
                {
                    var failed = new TextObject("{=VividWorld_Snapshots_SomeFailed}{COUNT} could not be deleted - see log.txt.");
                    failed.SetTextVariable("COUNT", result.Failed.Count);
                    InformationManager.DisplayMessage(new InformationMessage(failed.ToString()));
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Snapshot manager failed while deleting", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }

        private static void OnCancel()
        {
            _open = false;
            ModLog.Info(SnapshotManagerFormatter.FormatCancelled());
        }

        /// <summary>字串表取字；取不到就用英文退路（規格 §9.5）。</summary>
        private static string Text(string id, string fallback)
            => new TextObject("{=" + id + "}" + fallback).ToString();
    }
}
