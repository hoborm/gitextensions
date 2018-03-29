﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GitCommands;
using GitUI.Editor;
using ResourceManager;

namespace GitUI
{
    public static class GitUIExtensions
    {
        public static void OpenWithDifftool(this RevisionGrid grid, IReadOnlyList<GitRevision> revisions, string fileName, string oldFileName, GitUI.RevisionDiffKind diffKind, bool isTracked)
        {
            // Note: Order in revisions is that first clicked is last in array

            string error = RevisionDiffInfoProvider.Get(revisions, diffKind,
                out var extraDiffArgs, out var firstRevision, out var secondRevision);

            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show(grid, error);
            }
            else
            {
                string output = grid.Module.OpenWithDifftool(fileName, oldFileName, firstRevision, secondRevision, extraDiffArgs, isTracked);
                if (!string.IsNullOrEmpty(output))
                {
                    MessageBox.Show(grid, output);
                }
            }
        }

        private static PatchApply.Patch GetItemPatch(GitModule module, GitItemStatus file,
            string firstRevision, string secondRevision, string diffArgs, Encoding encoding)
        {
            // Files with tree guid should be presented with normal diff
            var isTracked = file.IsTracked || (file.TreeGuid.IsNotNullOrWhitespace() && secondRevision.IsNotNullOrWhitespace());
            return module.GetSingleDiff(firstRevision, secondRevision, file.Name, file.OldName,
                    diffArgs, encoding, true, isTracked);
        }

        private static string GetSelectedPatch(this FileViewer diffViewer, string firstRevision, string secondRevision, GitItemStatus file)
        {
            if (!file.IsTracked)
            {
                var fullPath = Path.Combine(diffViewer.Module.WorkingDir, file.Name);
                if (Directory.Exists(fullPath) && GitModule.IsValidGitWorkingDir(fullPath))
                {
                    // git-status does not detect details for untracked and git-diff --no-index will not give info
                    return LocalizationHelpers.GetSubmoduleText(diffViewer.Module, file.Name.TrimEnd('/'), "");
                }
            }

            if (file.IsSubmodule && file.GetSubmoduleStatusAsync() != null)
            {
                return LocalizationHelpers.ProcessSubmoduleStatus(diffViewer.Module, ThreadHelper.JoinableTaskFactory.Run(() => file.GetSubmoduleStatusAsync()));
            }

            PatchApply.Patch patch = GetItemPatch(diffViewer.Module, file, firstRevision, secondRevision,
                diffViewer.GetExtraDiffArguments(), diffViewer.Encoding);

            if (patch == null)
            {
                return string.Empty;
            }

            if (file.IsSubmodule)
            {
                return LocalizationHelpers.ProcessSubmodulePatch(diffViewer.Module, file.Name, patch);
            }

            return patch.Text;
        }

        public static Task ViewChangesAsync(this FileViewer diffViewer, IReadOnlyList<GitRevision> revisions, GitItemStatus file, string defaultText)
        {
            if (revisions.Count == 0)
            {
                return Task.CompletedTask;
            }

            var selectedRevision = revisions[0];
            string secondRevision = selectedRevision?.Guid;
            string firstRevision = revisions.Count >= 2 ? revisions[1].Guid : null;
            if (firstRevision == null && selectedRevision != null)
            {
                firstRevision = selectedRevision.FirstParentGuid;
            }

            return ViewChangesAsync(diffViewer, firstRevision, secondRevision, file, defaultText);
        }

        public static Task ViewChangesAsync(this FileViewer diffViewer, string firstRevision, string secondRevision, GitItemStatus file, string defaultText)
        {
            if (firstRevision == null)
            {
                // The previous commit does not exist, nothing to compare with
                if (file.TreeGuid.IsNullOrEmpty())
                {
                    if (secondRevision.IsNullOrWhiteSpace())
                    {
                        throw new ArgumentException(nameof(secondRevision));
                    }

                    return diffViewer.ViewGitItemRevisionAsync(file.Name, secondRevision);
                }
                else
                {
                    return diffViewer.ViewGitItemAsync(file.Name, file.TreeGuid);
                }
            }
            else
            {
                return diffViewer.ViewPatchAsync(() =>
                {
                    string selectedPatch = diffViewer.GetSelectedPatch(firstRevision, secondRevision, file);
                    return selectedPatch ?? defaultText;
                });
            }
        }

        public static void RemoveIfExists(this TabControl tabControl, TabPage page)
        {
            if (tabControl.TabPages.Contains(page))
            {
                tabControl.TabPages.Remove(page);
            }
        }

        public static void InsertIfNotExists(this TabControl tabControl, int index, TabPage page)
        {
            if (!tabControl.TabPages.Contains(page))
            {
                tabControl.TabPages.Insert(index, page);
            }
        }

        public static void Mask(this Control control)
        {
            if (FindMaskPanel(control) == null)
            {
                var panel = new MaskPanel { Dock = DockStyle.Fill };
                control.Controls.Add(panel);
                panel.BringToFront();
            }
        }

        public static void UnMask(this Control control)
        {
            MaskPanel panel = FindMaskPanel(control);
            if (panel != null)
            {
                control.Controls.Remove(panel);
                panel.Dispose();
            }
        }

        private static MaskPanel FindMaskPanel(Control control)
        {
            return control.Controls.Cast<Control>().OfType<MaskPanel>().FirstOrDefault();
        }

        private class MaskPanel : PictureBox
        {
            public MaskPanel()
            {
                Image = Properties.Resources.loadingpanel;
                SizeMode = PictureBoxSizeMode.CenterImage;
                BackColor = SystemColors.AppWorkspace;
            }
        }

        public static IEnumerable<TreeNode> AllNodes(this TreeView tree)
        {
            return tree.Nodes.AllNodes();
        }

        private static IEnumerable<TreeNode> AllNodes(this TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;

                foreach (TreeNode subNode in node.Nodes.AllNodes())
                {
                    yield return subNode;
                }
            }
        }

        public static async Task InvokeAsync(this Control control, Action action)
        {
            await control.SwitchToMainThreadAsync();
            action();
        }

        public static async Task InvokeAsync<T>(this Control control, Action<T> action, T state)
        {
            await control.SwitchToMainThreadAsync();
            action(state);
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        /// <summary>
        /// Use <see cref="InvokeAsync(Control, Action)"/> instead. If the result of
        /// <see cref="InvokeAsync(Control, Action)"/> is not awaited, use
        /// <see cref="ThreadHelper.FileAndForget(Task, Func{Exception, bool})"/> to ignore it.
        /// </summary>
        public static async void InvokeAsyncDoNotUseInNewCode(this Control control, Action action)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            if (ThreadHelper.JoinableTaskContext.IsOnMainThread)
            {
                await Task.Yield();
            }
            else
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            }

            if (control.IsDisposed)
            {
                return;
            }

            action();
        }

        public static void InvokeSync(this Control control, Action action)
        {
            ThreadHelper.JoinableTaskFactory.Run(
                async () =>
                {
                    try
                    {
                        await InvokeAsync(control, action);
                    }
                    catch (Exception e)
                    {
                        e.Data["StackTrace" + e.Data.Count] = e.StackTrace;
                        throw;
                    }
                });
        }

        public static Control FindFocusedControl(this ContainerControl container)
        {
            while (true)
            {
                if (container.ActiveControl is ContainerControl activeContainer)
                {
                    container = activeContainer;
                }
                else
                {
                    return container.ActiveControl;
                }
            }
        }
    }
}
