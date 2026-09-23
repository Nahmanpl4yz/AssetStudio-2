using AssetStudio;
using Newtonsoft.Json;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using static AssetStudioGUI.Studio;
using Font = AssetStudio.Font;
#if NET472
using Vector2 = OpenTK.Vector2;
using Vector3 = OpenTK.Vector3;
using Vector4 = OpenTK.Vector4;
#else
using Vector2 = OpenTK.Mathematics.Vector2;
using Vector3 = OpenTK.Mathematics.Vector3;
using Vector4 = OpenTK.Mathematics.Vector4;
using Matrix4 = OpenTK.Mathematics.Matrix4;
#endif

namespace AssetStudioGUI
{
    partial class AssetStudioGUIForm : Form
    {
        private AssetItem lastSelectedItem;
        private DirectBitmap imageTexture;
        private string tempClipboard;

        #region CombinedMeshParts (AssetStudio 2)
        // Overlay panel on top of glControl1 letting the user hide individual parts of a
        // combined GameObject/Mesh preview, and export only what's currently visible.
        private Panel meshPartsPanel;
        private CheckedListBox meshPartsCheckedListBox;
        private Button exportVisiblePartsButton;
        private Label meshPartsLabel;
        private List<GameObjectMeshCombiner.CombinedPart> currentCombinedParts;
        private GameObject currentCombinedRootGameObject;
        private readonly HashSet<long> hiddenPartPathIDs = new HashSet<long>();

        private sealed class MeshPartListItem
        {
            public readonly string Label;
            public readonly long PathID;
            public MeshPartListItem(string label, long pathID) { Label = label; PathID = pathID; }
            public override string ToString() => Label;
        }
        #endregion

        #region AnimationPreview (AssetStudio 2 - Feature 3)
        // Overlay panel with playback controls (play/pause, scrub bar, speed, loop) shown on
        // top of glControl1 whenever an AnimationClip (or Animator) is previewed. Driven by
        // animationPreviewTimer, which advances animationPreviewTime and re-skins the rig each
        // tick - see AnimationPreview.cs for the actual rig/clip/skinning logic.
        private Panel animationControlsPanel;
        private Button animationPlayPauseButton;
        private TrackBar animationScrubBar;
        private Label animationTimeLabel;
        private ComboBox animationSpeedCombo;
        private CheckBox animationLoopCheckBox;
        private ComboBox animationClipSelector;

        private AnimationPreview.Rig currentAnimationRig;
        private List<AnimationClip> currentAnimationClipCandidates;
        private AnimationPreview.DecodedClip currentDecodedClip;
        private System.Windows.Forms.Timer animationPreviewTimer;
        private float animationPreviewTime;
        private bool animationPreviewPlaying;
        private float animationPreviewSpeed = 1.0f;
        private bool animationPreviewLoop = true;
        private DateTime animationPreviewLastTick;
        private bool suppressAnimationScrubEvent;
        private bool animationBoundsFitted;
        #endregion

        private FMOD.System system;
        private FMOD.Sound sound;
        private FMOD.Channel channel;
        private FMOD.SoundGroup masterSoundGroup;
        private FMOD.MODE loopMode = FMOD.MODE.LOOP_OFF;
        private uint FMODlenms;
        private float FMODVolume = 0.8f;

        #region FMOD extras (AssetStudio 2 - Feature 5: waveform scrubbing + speed/pitch)
        // Waveform preview drawn above the FMOD transport, built once per clip from the
        // decoded PCM FMOD already holds in memory (see GenerateFMODWaveform). Click/drag
        // on it scrubs playback, mirroring FMODprogressBar's behavior but with a visual.
        private PictureBox FMODwaveformBox;
        private ComboBox FMODspeedCombo;
        private float[] FMODwaveformMin;
        private float[] FMODwaveformMax;
        private uint FMODbaseFrequency; // clip's native frequency, so speed changes are relative to it
        private float FMODSpeed = 1.0f;
        private bool FMODwaveformScrubbing;
        #endregion

        #region TexControl
        private static char[] textureChannelNames = new[] { 'B', 'G', 'R', 'A' };
        private bool[] textureChannels = new[] { true, true, true, true };
        #endregion

        #region GLControl
        private bool glControlLoaded;
        private int mdx, mdy;
        private bool lmdown, rmdown;
        private int pgmID, pgmColorID, pgmBlackID, pgmTexID;
        private int attributeVertexPosition;
        private int attributeNormalDirection;
        private int attributeVertexColor;
        private int attributeVertexTexCoord;
        private int uniformModelMatrix;
        private int uniformViewMatrix;
        private int uniformProjMatrix;
        private int uniformModelMatrixTex;
        private int uniformViewMatrixTex;
        private int uniformProjMatrixTex;
        private int vao;
        private Vector3[] vertexData;
        private Vector3[] normalData;
        private Vector3[] normal2Data;
        private Vector4[] colorData;
        private Vector2[] texCoordData;
        private Matrix4 modelMatrixData;
        private Matrix4 viewMatrixData;
        private Matrix4 projMatrixData;
        private int[] indiceData;
        private int wireFrameMode;
        private int shadeMode;

        //AssetStudio 2: Freecam mode fields.
        //Freecam uses a real camera position + yaw/pitch instead of the accumulated
        //orbit-style viewMatrixData multiplication used by the default camera, so we
        //keep the two systems separate and rebuild viewMatrixData from these each frame
        //while freecam is active.
        private bool freeCamMode;
        private Vector3 freeCamPos = new Vector3(0, 0, 3);
        private float freeCamYaw = (float)-Math.PI / 2; //looking down -Z initially
        private float freeCamPitch;
        private float freeCamSpeed = 2.0f; //units/second, adjustable with scroll wheel
        private Vector3 freeCamVelocity; //current smoothed velocity, for Unity-style accel/decel
        private const float FreeCamAccel = 12f; //how fast we ramp up to target speed
        private const float FreeCamDecel = 10f; //how fast we coast to a stop after keys are released
        private readonly HashSet<Keys> freeCamKeysDown = new HashSet<Keys>();
        private System.Windows.Forms.Timer freeCamTimer;
        private DateTime freeCamLastTick;
        private bool freeCamLooking; //right-mouse-button held = look + fly, matches Unity's Scene view flythrough
        private int normalMode;
        //AssetStudio 2: mesh + texture preview (per-submesh, one texture per submesh)
        private List<int> submeshTextureIds = new List<int>();
        private List<int> submeshIndexOffsets = new List<int>();
        private List<int> submeshIndexCounts = new List<int>();
        private bool meshPreviewHasAnyTexture;
        #endregion

        //asset list sorting
        private int sortColumn = -1;
        private bool reverseSort;

        //asset list filter
        private System.Timers.Timer delayTimer;
        private bool enableFiltering;

        //tree search
        private int nextGObject;
        private List<TreeNode> treeSrcResults = new List<TreeNode>();

        private string openDirectoryBackup = string.Empty;
        private string saveDirectoryBackup = string.Empty;

        private GUILogger logger;

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

        // AssetStudio 2: display version string shown in the title bar. Kept separate from the
        // numeric assembly/file version (which must stay a strict X.X.X.X for .NET) so the
        // title bar can show a human-friendly label like "Release 1.0" instead of "1.0.0.0".
        private const string AppDisplayVersion = "Release 1.0";

        public AssetStudioGUIForm()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            InitializeComponent();
            InitMeshPartsPanel();
            InitAnimationControlsPanel();
            InitFMODWaveformControls();
            InitScriptMappingMenu();
            Text = $"AssetStudio 2 {AppDisplayVersion}";
            delayTimer = new System.Timers.Timer(800);
            delayTimer.Elapsed += new ElapsedEventHandler(delayTimer_Elapsed);
            displayAll.Checked = Properties.Settings.Default.displayAll;
            displayInfo.Checked = Properties.Settings.Default.displayInfo;
            enablePreview.Checked = Properties.Settings.Default.enablePreview;
            previewMeshTexture.Checked = Properties.Settings.Default.previewMeshTexture;
            exportMeshWithTextures.Checked = Properties.Settings.Default.exportMeshWithTextures;
            var decoderMode = (DecoderMode)Properties.Settings.Default.decoderMode;
            Texture2DConverter.Mode = decoderMode;
            decoderModeBetter.Checked = decoderMode == DecoderMode.Better;
            decoderModeOriginal.Checked = decoderMode == DecoderMode.Original;
            FMODinit();

            logger = new GUILogger(StatusStripUpdate);
            Logger.Default = logger;
            Progress.Default = new Progress<int>(SetProgressBarValue);
            Studio.StatusStripUpdate = StatusStripUpdate;
        }

        private void AssetStudioGUIForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Move;
            }
        }

        private async void AssetStudioGUIForm_DragDrop(object sender, DragEventArgs e)
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths.Length > 0)
            {
                ResetForm();
                assetsManager.SpecifyUnityVersion = specifyUnityVersion.Text;
                if (paths.Length == 1 && Directory.Exists(paths[0]))
                {
                    await Task.Run(() => assetsManager.LoadFolder(paths[0]));
                }
                else
                {
                    await Task.Run(() => assetsManager.LoadFiles(paths));
                }
                BuildAssetStructures();
            }
        }

        private async void loadFile_Click(object sender, EventArgs e)
        {
            openFileDialog1.InitialDirectory = openDirectoryBackup;
            if (openFileDialog1.ShowDialog(this) == DialogResult.OK)
            {
                ResetForm();
                openDirectoryBackup = Path.GetDirectoryName(openFileDialog1.FileNames[0]);
                assetsManager.SpecifyUnityVersion = specifyUnityVersion.Text;
                await Task.Run(() => assetsManager.LoadFiles(openFileDialog1.FileNames));
                BuildAssetStructures();
            }
        }

        private async void loadFolder_Click(object sender, EventArgs e)
        {
            var openFolderDialog = new OpenFolderDialog();
            openFolderDialog.InitialFolder = openDirectoryBackup;
            if (openFolderDialog.ShowDialog(this) == DialogResult.OK)
            {
                ResetForm();
                openDirectoryBackup = openFolderDialog.Folder;
                assetsManager.SpecifyUnityVersion = specifyUnityVersion.Text;
                await Task.Run(() => assetsManager.LoadFolder(openFolderDialog.Folder));
                BuildAssetStructures();
            }
        }

        private async void extractFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog(this) == DialogResult.OK)
            {
                var saveFolderDialog = new OpenFolderDialog();
                saveFolderDialog.Title = "Select the save folder";
                if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    var fileNames = openFileDialog1.FileNames;
                    var savePath = saveFolderDialog.Folder;
                    var extractedCount = await Task.Run(() => ExtractFile(fileNames, savePath));
                    StatusStripUpdate($"Finished extracting {extractedCount} files.");
                }
            }
        }

        private async void extractFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var openFolderDialog = new OpenFolderDialog();
            if (openFolderDialog.ShowDialog(this) == DialogResult.OK)
            {
                var saveFolderDialog = new OpenFolderDialog();
                saveFolderDialog.Title = "Select the save folder";
                if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    var path = openFolderDialog.Folder;
                    var savePath = saveFolderDialog.Folder;
                    var extractedCount = await Task.Run(() => ExtractFolder(path, savePath));
                    StatusStripUpdate($"Finished extracting {extractedCount} files.");
                }
            }
        }

        private async void BuildAssetStructures()
        {
            if (assetsManager.assetsFileList.Count == 0)
            {
                StatusStripUpdate("No Unity file can be loaded.");
                return;
            }

            (var productName, var treeNodeCollection) = await Task.Run(() => BuildAssetData());
            var typeMap = await Task.Run(() => BuildClassStructure());

            if (!string.IsNullOrEmpty(productName))
            {
                Text = $"AssetStudio 2 {AppDisplayVersion} - {productName} - {assetsManager.assetsFileList[0].unityVersion} - {assetsManager.assetsFileList[0].m_TargetPlatform}";
            }
            else
            {
                Text = $"AssetStudio 2 {AppDisplayVersion} - no productName - {assetsManager.assetsFileList[0].unityVersion} - {assetsManager.assetsFileList[0].m_TargetPlatform}";
            }

            assetListView.VirtualListSize = visibleAssets.Count;

            sceneTreeView.BeginUpdate();
            sceneTreeView.Nodes.AddRange(treeNodeCollection.ToArray());
            sceneTreeView.EndUpdate();
            treeNodeCollection.Clear();

            classesListView.BeginUpdate();
            foreach (var version in typeMap)
            {
                var versionGroup = new ListViewGroup(version.Key);
                classesListView.Groups.Add(versionGroup);

                foreach (var uclass in version.Value)
                {
                    uclass.Value.Group = versionGroup;
                    classesListView.Items.Add(uclass.Value);
                }
            }
            typeMap.Clear();
            classesListView.EndUpdate();

            var types = exportableAssets.Select(x => x.Type).Distinct().OrderBy(x => x.ToString()).ToArray();
            foreach (var type in types)
            {
                var typeItem = new ToolStripMenuItem
                {
                    CheckOnClick = true,
                    Name = type.ToString(),
                    Size = new Size(180, 22),
                    Text = type.ToString()
                };
                typeItem.Click += typeToolStripMenuItem_Click;
                filterTypeToolStripMenuItem.DropDownItems.Add(typeItem);
            }
            allToolStripMenuItem.Checked = true;
            var log = $"Finished loading {assetsManager.assetsFileList.Count} files with {assetListView.Items.Count} exportable assets";
            var m_ObjectsCount = assetsManager.assetsFileList.Sum(x => x.m_Objects.Count);
            var objectsCount = assetsManager.assetsFileList.Sum(x => x.Objects.Count);
            if (m_ObjectsCount != objectsCount)
            {
                log += $" and {m_ObjectsCount - objectsCount} assets failed to read";
            }
            StatusStripUpdate(log);
        }

        private void typeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var typeItem = (ToolStripMenuItem)sender;
            if (typeItem != allToolStripMenuItem)
            {
                allToolStripMenuItem.Checked = false;
            }
            else if (allToolStripMenuItem.Checked)
            {
                for (var i = 1; i < filterTypeToolStripMenuItem.DropDownItems.Count; i++)
                {
                    var item = (ToolStripMenuItem)filterTypeToolStripMenuItem.DropDownItems[i];
                    item.Checked = false;
                }
            }
            FilterAssetList();
        }

        private void AssetStudioForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (glControl1.Visible)
            {
                if (e.Control)
                {
                    switch (e.KeyCode)
                    {
                        case Keys.W:
                            //Toggle WireFrame
                            wireFrameMode = (wireFrameMode + 1) % 3;
                            glControl1.Invalidate();
                            break;
                        case Keys.S:
                            //Toggle Shade
                            shadeMode = (shadeMode + 1) % 2;
                            glControl1.Invalidate();
                            break;
                        case Keys.N:
                            //Normal mode
                            normalMode = (normalMode + 1) % 2;
                            CreateVAO();
                            glControl1.Invalidate();
                            break;
                        case Keys.F:
                            //Toggle Freecam
                            ToggleFreeCam();
                            break;
                    }
                }
                else if (freeCamMode)
                {
                    if (e.KeyCode == Keys.F)
                    {
                        // AssetStudio 2 - Feature 8: Unity-style "F to frame" - snaps the
                        // freecam back to a good distance from the origin (where every mesh
                        // preview is centered via modelMatrixData) along the current look
                        // direction, without touching yaw/pitch, so you can back out of a
                        // model you've flown inside of or gotten lost around.
                        FreeCamFrameOrigin();
                    }
                    else
                    {
                        FreeCam_KeyDown(e.KeyCode, e.Shift);
                    }
                }
                else if (e.KeyCode == Keys.Space && animationControlsPanel != null && animationControlsPanel.Visible)
                {
                    // AssetStudio 2: Space toggles animation playback when the animation
                    // controls are showing, matching the play/pause button.
                    ToggleAnimationPlayback();
                    e.Handled = true;
                }
            }
            else if (previewPanel.Visible)
            {
                if (e.Control)
                {
                    var need = false;
                    switch (e.KeyCode)
                    {
                        case Keys.B:
                            textureChannels[0] = !textureChannels[0];
                            need = true;
                            break;
                        case Keys.G:
                            textureChannels[1] = !textureChannels[1];
                            need = true;
                            break;
                        case Keys.R:
                            textureChannels[2] = !textureChannels[2];
                            need = true;
                            break;
                        case Keys.A:
                            textureChannels[3] = !textureChannels[3];
                            need = true;
                            break;
                    }
                    if (need)
                    {
                        if (lastSelectedItem != null)
                        {
                            PreviewAsset(lastSelectedItem);
                            assetInfoLabel.Text = lastSelectedItem.InfoText;
                        }
                    }
                }
            }
        }

        private void exportClassStructuresMenuItem_Click(object sender, EventArgs e)
        {
            if (classesListView.Items.Count > 0)
            {
                var saveFolderDialog = new OpenFolderDialog();
                if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    var savePath = saveFolderDialog.Folder;
                    var count = classesListView.Items.Count;
                    int i = 0;
                    Progress.Reset();
                    foreach (TypeTreeItem item in classesListView.Items)
                    {
                        var versionPath = Path.Combine(savePath, item.Group.Header);
                        Directory.CreateDirectory(versionPath);

                        var saveFile = $"{versionPath}{Path.DirectorySeparatorChar}{item.SubItems[1].Text} {item.Text}.txt";
                        File.WriteAllText(saveFile, item.ToString());

                        Progress.Report(++i, count);
                    }

                    StatusStripUpdate("Finished exporting class structures");
                }
            }
        }

        private void displayAll_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.displayAll = displayAll.Checked;
            Properties.Settings.Default.Save();
        }

        private void decoderModeBetter_Click(object sender, EventArgs e)
        {
            SetDecoderMode(DecoderMode.Better);
        }

        private void decoderModeOriginal_Click(object sender, EventArgs e)
        {
            SetDecoderMode(DecoderMode.Original);
        }

        private void SetDecoderMode(DecoderMode mode)
        {
            Texture2DConverter.Mode = mode;
            decoderModeBetter.Checked = mode == DecoderMode.Better;
            decoderModeOriginal.Checked = mode == DecoderMode.Original;
            Properties.Settings.Default.decoderMode = (int)mode;
            Properties.Settings.Default.Save();

            //Re-render whatever is currently previewed so the effect is visible immediately.
            if (lastSelectedItem != null && lastSelectedItem.Type == ClassIDType.Texture2D)
            {
                PreviewAsset(lastSelectedItem);
            }
        }

        private void combineMeshPreview_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.combineMeshPreview = combineMeshPreview.Checked;
            Properties.Settings.Default.Save();
            if (lastSelectedItem != null && lastSelectedItem.Asset is Mesh m_Mesh)
            {
                PreviewMesh(m_Mesh);
            }
        }

        private void previewMeshTexture_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.previewMeshTexture = previewMeshTexture.Checked;
            Properties.Settings.Default.Save();
            if (lastSelectedItem != null && lastSelectedItem.Asset is Mesh m_Mesh)
            {
                PreviewMesh(m_Mesh);
            }
        }

        private void exportMeshWithTextures_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.exportMeshWithTextures = exportMeshWithTextures.Checked;
            Properties.Settings.Default.Save();
        }

        private void enablePreview_Check(object sender, EventArgs e)
        {
            if (lastSelectedItem != null)
            {
                switch (lastSelectedItem.Type)
                {
                    case ClassIDType.Texture2D:
                    case ClassIDType.Sprite:
                        {
                            if (enablePreview.Checked && imageTexture != null)
                            {
                                previewPanel.BackgroundImage = imageTexture.Bitmap;
                            }
                            else
                            {
                                previewPanel.BackgroundImage = Properties.Resources.preview;
                                previewPanel.BackgroundImageLayout = ImageLayout.Center;
                            }
                        }
                        break;
                    case ClassIDType.Shader:
                    case ClassIDType.TextAsset:
                    case ClassIDType.MonoBehaviour:
                        textPreviewBox.Visible = !textPreviewBox.Visible;
                        break;
                    case ClassIDType.Font:
                        fontPreviewBox.Visible = !fontPreviewBox.Visible;
                        break;
                    case ClassIDType.AudioClip:
                        {
                            FMODpanel.Visible = !FMODpanel.Visible;

                            if (sound != null && channel != null)
                            {
                                var result = channel.isPlaying(out var playing);
                                if (result == FMOD.RESULT.OK && playing)
                                {
                                    channel.stop();
                                    FMODreset();
                                }
                            }
                            else if (FMODpanel.Visible)
                            {
                                PreviewAsset(lastSelectedItem);
                            }

                            break;
                        }

                }

            }
            else if (lastSelectedItem != null && enablePreview.Checked)
            {
                PreviewAsset(lastSelectedItem);
            }

            Properties.Settings.Default.enablePreview = enablePreview.Checked;
            Properties.Settings.Default.Save();
        }

        private void displayAssetInfo_Check(object sender, EventArgs e)
        {
            if (displayInfo.Checked && assetInfoLabel.Text != null)
            {
                assetInfoLabel.Visible = true;
            }
            else
            {
                assetInfoLabel.Visible = false;
            }

            Properties.Settings.Default.displayInfo = displayInfo.Checked;
            Properties.Settings.Default.Save();
        }

        private void showExpOpt_Click(object sender, EventArgs e)
        {
            var exportOpt = new ExportOptions();
            exportOpt.ShowDialog(this);
        }

        private void assetListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = visibleAssets[e.ItemIndex];
        }

        private void tabPageSelected(object sender, TabControlEventArgs e)
        {
            switch (e.TabPageIndex)
            {
                case 0:
                    treeSearch.Select();
                    break;
                case 1:
                    listSearch.Select();
                    break;
            }
        }

        private void treeSearch_Enter(object sender, EventArgs e)
        {
            if (treeSearch.Text == " Search ")
            {
                treeSearch.Text = "";
                treeSearch.ForeColor = SystemColors.WindowText;
            }
        }

        private void treeSearch_Leave(object sender, EventArgs e)
        {
            if (treeSearch.Text == "")
            {
                treeSearch.Text = " Search ";
                treeSearch.ForeColor = SystemColors.GrayText;
            }
        }

        private void treeSearch_TextChanged(object sender, EventArgs e)
        {
            treeSrcResults.Clear();
            nextGObject = 0;
        }

        private void AssetStudioForm_KeyUp(object sender, KeyEventArgs e)
        {
            if (freeCamMode)
            {
                FreeCam_KeyUp(e.KeyCode);
            }
        }

        private void treeSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (treeSrcResults.Count == 0)
                {
                    foreach (TreeNode node in sceneTreeView.Nodes)
                    {
                        TreeNodeSearch(node);
                    }
                }
                if (treeSrcResults.Count > 0)
                {
                    if (nextGObject >= treeSrcResults.Count)
                    {
                        nextGObject = 0;
                    }
                    treeSrcResults[nextGObject].EnsureVisible();
                    sceneTreeView.SelectedNode = treeSrcResults[nextGObject];
                    nextGObject++;
                }
            }
        }

        private void TreeNodeSearch(TreeNode treeNode)
        {
            if (treeNode.Text.IndexOf(treeSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                treeSrcResults.Add(treeNode);
            }

            foreach (TreeNode node in treeNode.Nodes)
            {
                TreeNodeSearch(node);
            }
        }

        private void sceneTreeView_AfterCheck(object sender, TreeViewEventArgs e)
        {
            foreach (TreeNode childNode in e.Node.Nodes)
            {
                childNode.Checked = e.Node.Checked;
            }
        }

        private void listSearch_Enter(object sender, EventArgs e)
        {
            if (listSearch.Text == " Filter ")
            {
                listSearch.Text = "";
                listSearch.ForeColor = SystemColors.WindowText;
                enableFiltering = true;
            }
        }

        private void listSearch_Leave(object sender, EventArgs e)
        {
            if (listSearch.Text == "")
            {
                enableFiltering = false;
                listSearch.Text = " Filter ";
                listSearch.ForeColor = SystemColors.GrayText;
            }
        }

        private void ListSearchTextChanged(object sender, EventArgs e)
        {
            if (enableFiltering)
            {
                if (delayTimer.Enabled)
                {
                    delayTimer.Stop();
                    delayTimer.Start();
                }
                else
                {
                    delayTimer.Start();
                }
            }
        }

        private void delayTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            delayTimer.Stop();
            Invoke(new Action(FilterAssetList));
        }

        private void assetListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (sortColumn != e.Column)
            {
                reverseSort = false;
            }
            else
            {
                reverseSort = !reverseSort;
            }
            sortColumn = e.Column;
            assetListView.BeginUpdate();
            assetListView.SelectedIndices.Clear();
            if (sortColumn == 4) //FullSize
            {
                visibleAssets.Sort((a, b) =>
                {
                    var asf = a.FullSize;
                    var bsf = b.FullSize;
                    return reverseSort ? bsf.CompareTo(asf) : asf.CompareTo(bsf);
                });
            }
            else if (sortColumn == 3) // PathID
            {
                visibleAssets.Sort((x, y) =>
                {
                    long pathID_X = x.m_PathID;
                    long pathID_Y = y.m_PathID;
                    return reverseSort ? pathID_Y.CompareTo(pathID_X) : pathID_X.CompareTo(pathID_Y);
                });
            }
            else
            {
                visibleAssets.Sort((a, b) =>
                {
                    var at = a.SubItems[sortColumn].Text;
                    var bt = b.SubItems[sortColumn].Text;
                    return reverseSort ? bt.CompareTo(at) : at.CompareTo(bt);
                });
            }
            assetListView.EndUpdate();
        }

        private void selectAsset(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            previewPanel.BackgroundImage = Properties.Resources.preview;
            previewPanel.BackgroundImageLayout = ImageLayout.Center;
            classTextBox.Visible = false;
            assetInfoLabel.Visible = false;
            assetInfoLabel.Text = null;
            textPreviewBox.Visible = false;
            fontPreviewBox.Visible = false;
            FMODpanel.Visible = false;
            glControl1.Visible = false;
            if (meshPartsPanel != null) meshPartsPanel.Visible = false;
            currentCombinedParts = null;
            currentCombinedRootGameObject = null;
            StatusStripUpdate("");

            FMODreset();

            lastSelectedItem = (AssetItem)e.Item;

            if (e.IsSelected)
            {
                if (tabControl2.SelectedIndex == 1)
                {
                    dumpTextBox.Text = DumpAsset(lastSelectedItem.Asset);
                }
                if (enablePreview.Checked)
                {
                    PreviewAsset(lastSelectedItem);
                    if (displayInfo.Checked && lastSelectedItem.InfoText != null)
                    {
                        assetInfoLabel.Text = lastSelectedItem.InfoText;
                        assetInfoLabel.Visible = true;
                    }
                }
            }
        }

        private void classesListView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            classTextBox.Visible = true;
            assetInfoLabel.Visible = false;
            assetInfoLabel.Text = null;
            textPreviewBox.Visible = false;
            fontPreviewBox.Visible = false;
            FMODpanel.Visible = false;
            glControl1.Visible = false;
            if (meshPartsPanel != null) meshPartsPanel.Visible = false;
            currentCombinedParts = null;
            currentCombinedRootGameObject = null;
            StatusStripUpdate("");
            if (e.IsSelected)
            {
                classTextBox.Text = ((TypeTreeItem)classesListView.SelectedItems[0]).ToString();
            }
        }

        private void preview_Resize(object sender, EventArgs e)
        {
            if (glControlLoaded && glControl1.Visible)
            {
                ChangeGLSize(glControl1.Size);
                glControl1.Invalidate();
            }
            PositionMeshPartsPanel();
            PositionAnimationControlsPanel();
        }

        private void PreviewAsset(AssetItem assetItem)
        {
            if (assetItem == null)
                return;
            // AssetStudio 2: any selection that isn't an AnimationClip/Animator should stop
            // playback and hide the animation controls panel; each PreviewX below that *is*
            // animation-related re-shows/re-populates it as needed.
            if (!(assetItem.Asset is AnimationClip) && !(assetItem.Asset is Animator))
            {
                StopAnimationPreview();
            }
            try
            {
                switch (assetItem.Asset)
                {
                    case Texture2D m_Texture2D:
                        PreviewTexture2D(assetItem, m_Texture2D);
                        break;
                    case AudioClip m_AudioClip:
                        PreviewAudioClip(assetItem, m_AudioClip);
                        break;
                    case Shader m_Shader:
                        PreviewShader(m_Shader);
                        break;
                    case TextAsset m_TextAsset:
                        PreviewTextAsset(m_TextAsset);
                        break;
                    case MonoBehaviour m_MonoBehaviour:
                        PreviewMonoBehaviour(m_MonoBehaviour);
                        break;
                    case MonoScript m_MonoScript:
                        PreviewMonoScript(m_MonoScript);
                        break;
                    case Font m_Font:
                        PreviewFont(m_Font);
                        break;
                    case Mesh m_Mesh:
                        PreviewMesh(m_Mesh);
                        break;
                    case GameObject m_GameObject:
                        PreviewGameObject(m_GameObject);
                        break;
                    case VideoClip _:
                    case MovieTexture _:
                        StatusStripUpdate("Only supported export.");
                        break;
                    case Sprite m_Sprite:
                        PreviewSprite(assetItem, m_Sprite);
                        break;
                    case Material m_Material:
                        PreviewMaterial(assetItem, m_Material);
                        break;
                    case Animator m_Animator:
                        PreviewAnimator(m_Animator, null);
                        break;
                    case AnimationClip m_AnimationClip:
                        PreviewAnimationClip(m_AnimationClip);
                        break;
                    default:
                        var str = assetItem.Asset.Dump();
                        if (str != null)
                        {
                            textPreviewBox.Text = str;
                            textPreviewBox.Visible = true;
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"Preview {assetItem.Type}:{assetItem.Text} error\r\n{e.Message}\r\n{e.StackTrace}");
            }
        }

        // AssetStudio 2 - Feature 6: Material preview. Materials previously fell through to the
        // generic "default: dump text" case with no shader/property/texture summary and no
        // thumbnail. This resolves the shader name and every color/float/texture property via
        // the material's PPtrs, lists them in the info panel, and shows whichever texture looks
        // like the main one (falling back to the first texture slot) as an image thumbnail
        // through the same PreviewTexture(DirectBitmap) path Texture2D preview uses.
        private static readonly string[] LikelyMainTexNames = { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo", "_AlbedoMap" };

        private void PreviewMaterial(AssetItem assetItem, Material m_Material)
        {
            var sb = new StringBuilder();
            string shaderName = "Unknown";
            if (m_Material.m_Shader != null && m_Material.m_Shader.TryGet(out var shader))
            {
                shaderName = shader.m_Name;
            }
            sb.Append("Shader: ").Append(shaderName);

            var props = m_Material.m_SavedProperties;
            KeyValuePair<string, UnityTexEnv>? mainTexEntry = null;
            if (props != null)
            {
                if (props.m_TexEnvs != null && props.m_TexEnvs.Length > 0)
                {
                    sb.Append("\nTextures: ");
                    foreach (var kv in props.m_TexEnvs)
                    {
                        var hasTex = kv.Value?.m_Texture != null && kv.Value.m_Texture.m_PathID != 0;
                        sb.Append('\n').Append("  ").Append(kv.Key).Append(hasTex ? "" : " (none)");
                        if (hasTex && (mainTexEntry == null || LikelyMainTexNames.Contains(kv.Key)))
                        {
                            mainTexEntry = kv;
                        }
                    }
                }
                if (props.m_Colors != null && props.m_Colors.Length > 0)
                {
                    sb.Append("\nColors: ");
                    foreach (var kv in props.m_Colors)
                    {
                        sb.Append('\n').Append("  ").Append(kv.Key).Append(" = ")
                          .Append($"({kv.Value.R:0.###}, {kv.Value.G:0.###}, {kv.Value.B:0.###}, {kv.Value.A:0.###})");
                    }
                }
                if (props.m_Floats != null && props.m_Floats.Length > 0)
                {
                    sb.Append("\nFloats: ");
                    foreach (var kv in props.m_Floats)
                    {
                        sb.Append('\n').Append("  ").Append(kv.Key).Append(" = ").Append(kv.Value.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                }
            }
            assetItem.InfoText = sb.ToString();

            if (mainTexEntry != null && mainTexEntry.Value.Value.m_Texture.TryGet<Texture2D>(out var tex2d))
            {
                var image = tex2d.ConvertToImage(true);
                if (image != null)
                {
                    var bitmap = new DirectBitmap(image.ConvertToBytes(), tex2d.m_Width, tex2d.m_Height);
                    image.Dispose();
                    PreviewTexture(bitmap);
                    StatusStripUpdate($"Material preview: showing '{mainTexEntry.Value.Key}'");
                    return;
                }
            }

            StatusStripUpdate("Material preview: no texture to display, see info panel for properties");
        }

        private void PreviewTexture2D(AssetItem assetItem, Texture2D m_Texture2D)
        {
            var image = m_Texture2D.ConvertToImage(true);
            if (image != null)
            {
                var bitmap = new DirectBitmap(image.ConvertToBytes(), m_Texture2D.m_Width, m_Texture2D.m_Height);
                image.Dispose();
                assetItem.InfoText = $"Width: {m_Texture2D.m_Width}\nHeight: {m_Texture2D.m_Height}\nFormat: {m_Texture2D.m_TextureFormat}";
                switch (m_Texture2D.m_TextureSettings.m_FilterMode)
                {
                    case 0: assetItem.InfoText += "\nFilter Mode: Point "; break;
                    case 1: assetItem.InfoText += "\nFilter Mode: Bilinear "; break;
                    case 2: assetItem.InfoText += "\nFilter Mode: Trilinear "; break;
                }
                assetItem.InfoText += $"\nAnisotropic level: {m_Texture2D.m_TextureSettings.m_Aniso}\nMip map bias: {m_Texture2D.m_TextureSettings.m_MipBias}";
                switch (m_Texture2D.m_TextureSettings.m_WrapMode)
                {
                    case 0: assetItem.InfoText += "\nWrap mode: Repeat"; break;
                    case 1: assetItem.InfoText += "\nWrap mode: Clamp"; break;
                }
                assetItem.InfoText += "\nChannels: ";
                int validChannel = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (textureChannels[i])
                    {
                        assetItem.InfoText += textureChannelNames[i];
                        validChannel++;
                    }
                }
                if (validChannel == 0)
                    assetItem.InfoText += "None";
                if (validChannel != 4)
                {
                    var bytes = bitmap.Bits;
                    for (int i = 0; i < bitmap.Height; i++)
                    {
                        int offset = Math.Abs(bitmap.Stride) * i;
                        for (int j = 0; j < bitmap.Width; j++)
                        {
                            bytes[offset] = textureChannels[0] ? bytes[offset] : validChannel == 1 && textureChannels[3] ? byte.MaxValue : byte.MinValue;
                            bytes[offset + 1] = textureChannels[1] ? bytes[offset + 1] : validChannel == 1 && textureChannels[3] ? byte.MaxValue : byte.MinValue;
                            bytes[offset + 2] = textureChannels[2] ? bytes[offset + 2] : validChannel == 1 && textureChannels[3] ? byte.MaxValue : byte.MinValue;
                            bytes[offset + 3] = textureChannels[3] ? bytes[offset + 3] : byte.MaxValue;
                            offset += 4;
                        }
                    }
                }
                PreviewTexture(bitmap);

                StatusStripUpdate("'Ctrl'+'R'/'G'/'B'/'A' for Channel Toggle");
            }
            else
            {
                StatusStripUpdate("Unsupported image for preview");
            }
        }

        private void PreviewAudioClip(AssetItem assetItem, AudioClip m_AudioClip)
        {
            //Info
            assetItem.InfoText = "Compression format: ";
            if (m_AudioClip.version[0] < 5)
            {
                switch (m_AudioClip.m_Type)
                {
                    case FMODSoundType.ACC:
                        assetItem.InfoText += "Acc";
                        break;
                    case FMODSoundType.AIFF:
                        assetItem.InfoText += "AIFF";
                        break;
                    case FMODSoundType.IT:
                        assetItem.InfoText += "Impulse tracker";
                        break;
                    case FMODSoundType.MOD:
                        assetItem.InfoText += "Protracker / Fasttracker MOD";
                        break;
                    case FMODSoundType.MPEG:
                        assetItem.InfoText += "MP2/MP3 MPEG";
                        break;
                    case FMODSoundType.OGGVORBIS:
                        assetItem.InfoText += "Ogg vorbis";
                        break;
                    case FMODSoundType.S3M:
                        assetItem.InfoText += "ScreamTracker 3";
                        break;
                    case FMODSoundType.WAV:
                        assetItem.InfoText += "Microsoft WAV";
                        break;
                    case FMODSoundType.XM:
                        assetItem.InfoText += "FastTracker 2 XM";
                        break;
                    case FMODSoundType.XMA:
                        assetItem.InfoText += "Xbox360 XMA";
                        break;
                    case FMODSoundType.VAG:
                        assetItem.InfoText += "PlayStation Portable ADPCM";
                        break;
                    case FMODSoundType.AUDIOQUEUE:
                        assetItem.InfoText += "iPhone";
                        break;
                    default:
                        assetItem.InfoText += "Unknown";
                        break;
                }
            }
            else
            {
                switch (m_AudioClip.m_CompressionFormat)
                {
                    case AudioCompressionFormat.PCM:
                        assetItem.InfoText += "PCM";
                        break;
                    case AudioCompressionFormat.Vorbis:
                        assetItem.InfoText += "Vorbis";
                        break;
                    case AudioCompressionFormat.ADPCM:
                        assetItem.InfoText += "ADPCM";
                        break;
                    case AudioCompressionFormat.MP3:
                        assetItem.InfoText += "MP3";
                        break;
                    case AudioCompressionFormat.PSMVAG:
                        assetItem.InfoText += "PlayStation Portable ADPCM";
                        break;
                    case AudioCompressionFormat.HEVAG:
                        assetItem.InfoText += "PSVita ADPCM";
                        break;
                    case AudioCompressionFormat.XMA:
                        assetItem.InfoText += "Xbox360 XMA";
                        break;
                    case AudioCompressionFormat.AAC:
                        assetItem.InfoText += "AAC";
                        break;
                    case AudioCompressionFormat.GCADPCM:
                        assetItem.InfoText += "Nintendo 3DS/Wii DSP";
                        break;
                    case AudioCompressionFormat.ATRAC9:
                        assetItem.InfoText += "PSVita ATRAC9";
                        break;
                    default:
                        assetItem.InfoText += "Unknown";
                        break;
                }
            }

            var m_AudioData = m_AudioClip.m_AudioData.GetData();
            if (m_AudioData == null || m_AudioData.Length == 0)
                return;
            var exinfo = new FMOD.CREATESOUNDEXINFO();

            exinfo.cbsize = Marshal.SizeOf(exinfo);
            exinfo.length = (uint)m_AudioClip.m_Size;

            var result = system.createSound(m_AudioData, FMOD.MODE.OPENMEMORY | loopMode, ref exinfo, out sound);
            if (ERRCHECK(result)) return;

            sound.getNumSubSounds(out var numsubsounds);

            if (numsubsounds > 0)
            {
                result = sound.getSubSound(0, out var subsound);
                if (result == FMOD.RESULT.OK)
                {
                    sound = subsound;
                }
            }

            result = sound.getLength(out FMODlenms, FMOD.TIMEUNIT.MS);
            if (ERRCHECK(result)) return;

            result = system.playSound(sound, null, true, out channel);
            if (ERRCHECK(result)) return;

            FMODpanel.Visible = true;

            result = channel.getFrequency(out var frequency);
            if (ERRCHECK(result)) return;

            FMODinfoLabel.Text = frequency + " Hz";
            FMODtimerLabel.Text = $"0:0.0 / {FMODlenms / 1000 / 60}:{FMODlenms / 1000 % 60}.{FMODlenms / 10 % 100}";

            //AssetStudio 2 - Feature 5: waveform + speed/pitch.
            FMODbaseFrequency = (uint)frequency;
            if (FMODspeedCombo != null) { FMODspeedCombo.SelectedIndex = 2; } // reset to 1x for the new clip
            FMODSpeed = 1.0f;
            GenerateFMODWaveform(sound);
            FMODwaveformBox?.Invalidate();
        }

        private void PreviewShader(Shader m_Shader)
        {
            var str = ShaderConverter.Convert(m_Shader);
            PreviewText(str == null ? "Serialized Shader can't be read" : str.Replace("\n", "\r\n"));
        }

        private void PreviewTextAsset(TextAsset m_TextAsset)
        {
            var text = Encoding.UTF8.GetString(m_TextAsset.m_Script);
            text = text.Replace("\n", "\r\n").Replace("\0", "");
            PreviewText(text);
        }

        private void PreviewMonoBehaviour(MonoBehaviour m_MonoBehaviour)
        {
            var obj = m_MonoBehaviour.ToType();
            if (obj == null)
            {
                var type = MonoBehaviourToTypeTree(m_MonoBehaviour);
                obj = m_MonoBehaviour.ToType(type);
            }
            var str = JsonConvert.SerializeObject(obj, Formatting.Indented);
            PreviewText(str);
        }

        // AssetStudio 2: preview for a MonoScript asset itself (as opposed to a MonoBehaviour
        // instance of it). Auto-maps the script's fields straight from the game's managed
        // assemblies (no manual DLL browsing needed - see Studio.EnsureAssembliesAutoLoaded)
        // and cross-references every loaded MonoBehaviour instance that uses this script.
        private void PreviewMonoScript(MonoScript m_MonoScript)
        {
            var fullName = string.IsNullOrEmpty(m_MonoScript.m_Namespace)
                ? m_MonoScript.m_ClassName
                : $"{m_MonoScript.m_Namespace}.{m_MonoScript.m_ClassName}";

            var sb = new StringBuilder();
            sb.AppendLine($"Class:    {fullName}");
            sb.AppendLine($"Assembly: {m_MonoScript.m_AssemblyName}");
            sb.AppendLine();

            EnsureAssembliesAutoLoaded();
            if (!assemblyLoader.Loaded)
            {
                sb.AppendLine("Fields: unavailable - couldn't auto-detect a 'Managed' folder next to the loaded files.");
                sb.AppendLine("Use Options > Load assembly folder for script mapping... to map fields manually.");
            }
            else
            {
                var typeDef = assemblyLoader.GetTypeDefinition(m_MonoScript.m_AssemblyName, fullName);
                if (typeDef == null)
                {
                    sb.AppendLine($"Fields: unavailable - '{fullName}' wasn't found in the loaded assemblies.");
                    sb.AppendLine("(mismatched build, obfuscated names, or an IL2CPP build with no per-script DLLs)");
                }
                else
                {
                    sb.AppendLine("Fields (auto-mapped from assembly metadata):");
                    try
                    {
                        var helper = new SerializedTypeHelper(m_MonoScript.version);
                        var converter = new TypeDefinitionConverter(typeDef, helper, 1);
                        var fieldNodes = converter.ConvertToTypeTreeNodes().ToList();
                        if (fieldNodes.Count == 0)
                        {
                            sb.AppendLine("  (no serialized fields)");
                        }
                        else
                        {
                            foreach (var node in fieldNodes)
                            {
                                sb.AppendLine(new string(' ', Math.Max(0, node.m_Level - 1) * 4) + node.m_Type + " " + node.m_Name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("  Failed to map fields: " + ex.Message);
                    }
                }
            }

            var instances = FindMonoBehavioursUsingScript(m_MonoScript);
            sb.AppendLine();
            if (instances.Count == 0)
            {
                sb.AppendLine("No loaded MonoBehaviour instances reference this script.");
            }
            else
            {
                sb.AppendLine($"Used by {instances.Count} loaded MonoBehaviour instance(s):");
                foreach (var inst in instances.Take(100))
                {
                    sb.AppendLine("  - " + (string.IsNullOrEmpty(inst.m_Name) ? "(unnamed)" : inst.m_Name));
                }
                if (instances.Count > 100) sb.AppendLine($"  ... and {instances.Count - 100} more");
            }

            PreviewText(sb.ToString());
        }

        // Scans every loaded file for MonoBehaviour instances whose m_Script points at this
        // MonoScript. Mirrors the reference-equality lookup GameObjectMeshCombiner uses for
        // meshes - deserialized assets are cached per PathID by AssetsManager, so a plain "=="
        // is enough (no cross-file PathID bookkeeping needed).
        private static List<MonoBehaviour> FindMonoBehavioursUsingScript(MonoScript script)
        {
            var result = new List<MonoBehaviour>();
            foreach (var assetsFile in assetsManager.assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (obj is MonoBehaviour mb && mb.m_Script.TryGet(out var s) && s == script)
                    {
                        result.Add(mb);
                    }
                }
            }
            return result;
        }

        private void PreviewFont(Font m_Font)
        {
            if (m_Font.m_FontData != null)
            {
                var data = Marshal.AllocCoTaskMem(m_Font.m_FontData.Length);
                Marshal.Copy(m_Font.m_FontData, 0, data, m_Font.m_FontData.Length);

                uint cFonts = 0;
                var re = AddFontMemResourceEx(data, (uint)m_Font.m_FontData.Length, IntPtr.Zero, ref cFonts);
                if (re != IntPtr.Zero)
                {
                    using (var pfc = new PrivateFontCollection())
                    {
                        pfc.AddMemoryFont(data, m_Font.m_FontData.Length);
                        Marshal.FreeCoTaskMem(data);
                        if (pfc.Families.Length > 0)
                        {
                            fontPreviewBox.SelectionStart = 0;
                            fontPreviewBox.SelectionLength = 80;
                            fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 16, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 81;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 12, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 138;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 18, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 195;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 24, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 252;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 36, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 309;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 48, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 366;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 60, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 423;
                            fontPreviewBox.SelectionLength = 55;
                            fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 72, FontStyle.Regular);
                            fontPreviewBox.Visible = true;
                        }
                    }
                    return;
                }
            }
            StatusStripUpdate("Unsupported font for preview. Try to export.");
        }

        // AssetStudio 2: manual override/fallback for script field mapping, next to the
        // existing "Combine sibling meshes" option. Auto-mapping (Studio.EnsureAssembliesAutoLoaded)
        // covers the common case; this lets the user point at a Managed folder by hand when
        // auto-detection can't find one (bundle-only exports, unusual folder layouts, etc.).
        private void InitScriptMappingMenu()
        {
            var loadAssembliesItem = new ToolStripMenuItem("Load assembly folder for script mapping...");
            loadAssembliesItem.Click += (s, e) =>
            {
                var dlg = new OpenFolderDialog { Title = "Select Managed Assembly Folder" };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                assemblyLoader.Clear();
                assemblyLoader.Load(dlg.Folder);
                StatusStripUpdate(assemblyLoader.Loaded
                    ? $"Loaded assemblies from: {dlg.Folder}"
                    : "No assemblies found in that folder.");

                if (lastSelectedItem != null) PreviewAsset(lastSelectedItem);
            };
            optionsToolStripMenuItem.DropDownItems.Add(loadAssembliesItem);
        }

        // AssetStudio 2: builds the "parts" overlay (checklist + export button) purely in code
        // and docks it on top of glControl1 inside previewPanel, so the visual designer file
        // doesn't need to be touched. Called once from the constructor, after InitializeComponent.
        private void InitMeshPartsPanel()
        {
            meshPartsPanel = new Panel
            {
                BackColor = System.Drawing.Color.FromArgb(235, 32, 32, 32),
                Width = 230,
                Visible = false
            };
            meshPartsLabel = new Label
            {
                Text = "Parts (uncheck to hide)",
                Dock = DockStyle.Top,
                ForeColor = System.Drawing.Color.White,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            exportVisiblePartsButton = new Button
            {
                Text = "Export Visible...",
                Dock = DockStyle.Bottom,
                Height = 28
            };
            exportVisiblePartsButton.Click += exportVisiblePartsButton_Click;
            meshPartsCheckedListBox = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BackColor = System.Drawing.Color.FromArgb(45, 45, 45),
                ForeColor = System.Drawing.Color.White,
                BorderStyle = BorderStyle.None
            };
            meshPartsCheckedListBox.ItemCheck += meshPartsCheckedListBox_ItemCheck;

            meshPartsPanel.Controls.Add(meshPartsCheckedListBox);
            meshPartsPanel.Controls.Add(exportVisiblePartsButton);
            meshPartsPanel.Controls.Add(meshPartsLabel);

            previewPanel.Controls.Add(meshPartsPanel);
            meshPartsPanel.BringToFront();
            PositionMeshPartsPanel();
        }

        // Keeps the overlay pinned to the top-right corner of the preview area, full height.
        private void PositionMeshPartsPanel()
        {
            if (meshPartsPanel == null || previewPanel == null) return;
            meshPartsPanel.Location = new Point(Math.Max(0, previewPanel.ClientSize.Width - meshPartsPanel.Width), 0);
            meshPartsPanel.Height = previewPanel.ClientSize.Height;
        }

        // A part's checkbox changed - update the hidden set and re-render immediately.
        private void meshPartsCheckedListBox_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (e.Index < 0 || e.Index >= meshPartsCheckedListBox.Items.Count) return;
            if (!(meshPartsCheckedListBox.Items[e.Index] is MeshPartListItem item)) return;

            if (e.NewValue == CheckState.Unchecked) hiddenPartPathIDs.Add(item.PathID);
            else hiddenPartPathIDs.Remove(item.PathID);

            // ItemCheck fires just before the state is actually committed; render on the next
            // tick so CreateVAO() etc. don't race the checkbox's own visual update.
            BeginInvoke((MethodInvoker)RenderCombinedParts);
        }

        // Exports the combined object, leaving out whatever parts are currently unchecked.
        private void exportVisiblePartsButton_Click(object sender, EventArgs e)
        {
            if (currentCombinedRootGameObject == null)
            {
                StatusStripUpdate("Nothing to export.");
                return;
            }
            if (hiddenPartPathIDs.Count == currentCombinedParts?.Count)
            {
                StatusStripUpdate("All parts are hidden - nothing to export.");
                return;
            }

            var saveFolderDialog = new OpenFolderDialog();
            saveFolderDialog.InitialFolder = saveDirectoryBackup;
            if (saveFolderDialog.ShowDialog(this) != DialogResult.OK) return;
            saveDirectoryBackup = saveFolderDialog.Folder;
            var exportPath = Path.Combine(saveFolderDialog.Folder, "GameObject") + Path.DirectorySeparatorChar;

            try
            {
                Exporter.ExportGameObject(currentCombinedRootGameObject, exportPath, null, new HashSet<long>(hiddenPartPathIDs));
                var shown = (currentCombinedParts?.Count ?? 0) - hiddenPartPathIDs.Count;
                StatusStripUpdate($"Exported {shown}/{currentCombinedParts?.Count ?? 0} visible parts to {exportPath}");
            }
            catch (Exception ex)
            {
                StatusStripUpdate("Export failed: " + ex.Message);
            }
        }

        // AssetStudio 2: entry point for selecting a GameObject directly in the tree - always
        // combines every mesh under it (ignores the toggle, since there's no "single mesh"
        // fallback to speak of here).
        private void PreviewGameObject(GameObject m_GameObject)
        {
            try
            {
                var root = GameObjectMeshCombiner.FindRootTransform(m_GameObject) ?? GameObjectMeshCombiner.GetTransform(m_GameObject);
                if (root == null)
                {
                    StatusStripUpdate("GameObject has no Transform, can't be previewed.");
                    return;
                }
                var parts = GameObjectMeshCombiner.CollectParts(root);
                if (parts.Count == 0)
                {
                    StatusStripUpdate("No meshes found under this GameObject.");
                    return;
                }

                currentCombinedParts = parts;
                currentCombinedRootGameObject = root.m_GameObject.TryGet(out var rootGo) ? rootGo : m_GameObject;
                PopulateMeshPartsList(parts);
                RenderCombinedParts();
            }
            catch (Exception ex)
            {
                StatusStripUpdate("Failed to preview GameObject: " + ex.Message);
            }
        }

        // AssetStudio 2: (re)builds the GL buffers from currentCombinedParts, skipping any part
        // whose owning GameObject is in hiddenPartPathIDs. Shared by the initial combined preview
        // and by the parts checklist whenever the user toggles a part on/off.
        private void RenderCombinedParts()
        {
            if (currentCombinedParts == null || currentCombinedParts.Count == 0) return;

            var combined = GameObjectMeshCombiner.Combine(currentCombinedParts, hiddenPartPathIDs);
            if (combined.Vertices.Length == 0)
            {
                glControl1.Visible = false;
                StatusStripUpdate("All parts hidden - nothing to preview.");
                return;
            }

            viewMatrixData = Matrix4.CreateRotationY(-(float)Math.PI / 4) * Matrix4.CreateRotationX(-(float)Math.PI / 6);
            vertexData = combined.Vertices;
            normalData = combined.Normals;
            normal2Data = combined.Normals;
            colorData = combined.Colors;
            indiceData = combined.Indices;
            texCoordData = null; // no single Mesh to resolve submesh textures against here

            float[] min = { vertexData[0].X, vertexData[0].Y, vertexData[0].Z };
            float[] max = { vertexData[0].X, vertexData[0].Y, vertexData[0].Z };
            foreach (var v in vertexData)
            {
                min[0] = Math.Min(min[0], v.X); max[0] = Math.Max(max[0], v.X);
                min[1] = Math.Min(min[1], v.Y); max[1] = Math.Max(max[1], v.Y);
                min[2] = Math.Min(min[2], v.Z); max[2] = Math.Max(max[2], v.Z);
            }
            Vector3 dist = new Vector3(max[0] - min[0], max[1] - min[1], max[2] - min[2]);
            Vector3 offset = new Vector3((max[0] + min[0]) / 2, (max[1] + min[1]) / 2, (max[2] + min[2]) / 2);
            float d = Math.Max(1e-5f, dist.Length);
            modelMatrixData = Matrix4.CreateTranslation(-offset) * Matrix4.CreateScale(2f / d);

            glControl1.Visible = true;
            CreateVAO();
            var shown = currentCombinedParts.Count - hiddenPartPathIDs.Count;
            StatusStripUpdate("Using OpenGL Version: " + GL.GetString(StringName.Version) + "\n"
                              + $"Combined preview ({shown}/{currentCombinedParts.Count} meshes shown) \n"
                              + "'Mouse Left'=Rotate | 'Mouse Right'=Move | 'Mouse Wheel'=Zoom \n"
                              + "'Ctrl W'=Wireframe | 'Ctrl S'=Shade | 'Ctrl N'=ReNormal ");
        }

        // AssetStudio 2: fills the overlay checklist with one entry per combined part (deduped
        // display names get a "(2)", "(3)"... suffix) and shows/hides the overlay panel itself.
        private void PopulateMeshPartsList(List<GameObjectMeshCombiner.CombinedPart> parts)
        {
            if (meshPartsCheckedListBox == null) return;

            meshPartsCheckedListBox.ItemCheck -= meshPartsCheckedListBox_ItemCheck;
            meshPartsCheckedListBox.Items.Clear();
            hiddenPartPathIDs.Clear();

            var seen = new Dictionary<string, int>();
            foreach (var part in parts)
            {
                var label = string.IsNullOrEmpty(part.Name) ? "(unnamed)" : part.Name;
                if (seen.TryGetValue(label, out var n))
                {
                    seen[label] = n + 1;
                    label = $"{label} ({n + 1})";
                }
                else
                {
                    seen[label] = 0;
                }
                meshPartsCheckedListBox.Items.Add(new MeshPartListItem(label, part.GameObjectPathID), true);
            }

            meshPartsPanel.Visible = parts.Count > 1;
            if (meshPartsPanel.Visible)
            {
                PositionMeshPartsPanel();
                meshPartsPanel.BringToFront();
            }
            meshPartsCheckedListBox.ItemCheck += meshPartsCheckedListBox_ItemCheck;
        }

        private void PreviewMesh(Mesh m_Mesh)
        {
            // AssetStudio 2: if "combine sibling meshes" is enabled, try to locate the
            // GameObject that owns this mesh and merge every mesh under its transform
            // hierarchy (wheels, interior, etc.) into one combined buffer, aligned the same
            // way Unity assembles them at runtime. Falls back to the plain single-mesh
            // preview if the mesh isn't referenced by any loaded GameObject, or combining fails.
            List<GameObjectMeshCombiner.CombinedPart> parts = null;
            GameObject combinedRootGo = null;
            if (Properties.Settings.Default.combineMeshPreview)
            {
                try
                {
                    var owner = GameObjectMeshCombiner.FindOwningGameObject(m_Mesh, assetsManager);
                    if (owner != null)
                    {
                        var root = GameObjectMeshCombiner.FindRootTransform(owner);
                        if (root != null)
                        {
                            var collected = GameObjectMeshCombiner.CollectParts(root);
                            if (collected.Count > 1)
                            {
                                parts = collected;
                                combinedRootGo = root.m_GameObject.TryGet(out var rootGo) ? rootGo : owner;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    parts = null;
                    StatusStripUpdate("Combine preview failed, falling back to single mesh: " + ex.Message);
                }
            }

            if (parts != null)
            {
                currentCombinedParts = parts;
                currentCombinedRootGameObject = combinedRootGo;
                PopulateMeshPartsList(parts);
                RenderCombinedParts();
                return;
            }

            // Not combining (toggle off, no owner found, or only a single part) - fall back to
            // the plain single-mesh preview below, and make sure the parts overlay is hidden.
            currentCombinedParts = null;
            currentCombinedRootGameObject = null;
            if (meshPartsPanel != null) meshPartsPanel.Visible = false;

            if (m_Mesh.m_VertexCount > 0)
            {
                viewMatrixData = Matrix4.CreateRotationY(-(float)Math.PI / 4) * Matrix4.CreateRotationX(-(float)Math.PI / 6);
                #region Vertices
                if (m_Mesh.m_Vertices == null || m_Mesh.m_Vertices.Length == 0)
                {
                    StatusStripUpdate("Mesh can't be previewed.");
                    return;
                }
                int count = 3;
                if (m_Mesh.m_Vertices.Length == m_Mesh.m_VertexCount * 4)
                {
                    count = 4;
                }
                vertexData = new Vector3[m_Mesh.m_VertexCount];
                // Calculate Bounding
                float[] min = new float[3];
                float[] max = new float[3];
                for (int i = 0; i < 3; i++)
                {
                    min[i] = m_Mesh.m_Vertices[i];
                    max[i] = m_Mesh.m_Vertices[i];
                }
                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        min[i] = Math.Min(min[i], m_Mesh.m_Vertices[v * count + i]);
                        max[i] = Math.Max(max[i], m_Mesh.m_Vertices[v * count + i]);
                    }
                    vertexData[v] = new Vector3(
                        m_Mesh.m_Vertices[v * count],
                        m_Mesh.m_Vertices[v * count + 1],
                        m_Mesh.m_Vertices[v * count + 2]);
                }

                // Calculate modelMatrix
                Vector3 dist = Vector3.One, offset = Vector3.Zero;
                for (int i = 0; i < 3; i++)
                {
                    dist[i] = max[i] - min[i];
                    offset[i] = (max[i] + min[i]) / 2;
                }
                float d = Math.Max(1e-5f, dist.Length);
                modelMatrixData = Matrix4.CreateTranslation(-offset) * Matrix4.CreateScale(2f / d);
                #endregion
                #region Indicies
                indiceData = new int[m_Mesh.m_Indices.Count];
                for (int i = 0; i < m_Mesh.m_Indices.Count; i = i + 3)
                {
                    indiceData[i] = (int)m_Mesh.m_Indices[i];
                    indiceData[i + 1] = (int)m_Mesh.m_Indices[i + 1];
                    indiceData[i + 2] = (int)m_Mesh.m_Indices[i + 2];
                }
                #endregion
                #region Normals
                if (m_Mesh.m_Normals != null && m_Mesh.m_Normals.Length > 0)
                {
                    if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 3)
                        count = 3;
                    else if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 4)
                        count = 4;
                    normalData = new Vector3[m_Mesh.m_VertexCount];
                    for (int n = 0; n < m_Mesh.m_VertexCount; n++)
                    {
                        normalData[n] = new Vector3(
                            m_Mesh.m_Normals[n * count],
                            m_Mesh.m_Normals[n * count + 1],
                            m_Mesh.m_Normals[n * count + 2]);
                    }
                }
                else
                    normalData = null;
                // calculate normal by ourself
                normal2Data = new Vector3[m_Mesh.m_VertexCount];
                int[] normalCalculatedCount = new int[m_Mesh.m_VertexCount];
                for (int i = 0; i < m_Mesh.m_VertexCount; i++)
                {
                    normal2Data[i] = Vector3.Zero;
                    normalCalculatedCount[i] = 0;
                }
                for (int i = 0; i < m_Mesh.m_Indices.Count; i = i + 3)
                {
                    Vector3 dir1 = vertexData[indiceData[i + 1]] - vertexData[indiceData[i]];
                    Vector3 dir2 = vertexData[indiceData[i + 2]] - vertexData[indiceData[i]];
                    Vector3 normal = Vector3.Cross(dir1, dir2);
                    normal.Normalize();
                    for (int j = 0; j < 3; j++)
                    {
                        normal2Data[indiceData[i + j]] += normal;
                        normalCalculatedCount[indiceData[i + j]]++;
                    }
                }
                for (int i = 0; i < m_Mesh.m_VertexCount; i++)
                {
                    if (normalCalculatedCount[i] == 0)
                        normal2Data[i] = new Vector3(0, 1, 0);
                    else
                        normal2Data[i] /= normalCalculatedCount[i];
                }
                #endregion
                #region Colors
                if (m_Mesh.m_Colors != null && m_Mesh.m_Colors.Length == m_Mesh.m_VertexCount * 3)
                {
                    colorData = new Vector4[m_Mesh.m_VertexCount];
                    for (int c = 0; c < m_Mesh.m_VertexCount; c++)
                    {
                        colorData[c] = new Vector4(
                            m_Mesh.m_Colors[c * 3],
                            m_Mesh.m_Colors[c * 3 + 1],
                            m_Mesh.m_Colors[c * 3 + 2],
                            1.0f);
                    }
                }
                else if (m_Mesh.m_Colors != null && m_Mesh.m_Colors.Length == m_Mesh.m_VertexCount * 4)
                {
                    colorData = new Vector4[m_Mesh.m_VertexCount];
                    for (int c = 0; c < m_Mesh.m_VertexCount; c++)
                    {
                        colorData[c] = new Vector4(
                        m_Mesh.m_Colors[c * 4],
                        m_Mesh.m_Colors[c * 4 + 1],
                        m_Mesh.m_Colors[c * 4 + 2],
                        m_Mesh.m_Colors[c * 4 + 3]);
                    }
                }
                else
                {
                    colorData = new Vector4[m_Mesh.m_VertexCount];
                    for (int c = 0; c < m_Mesh.m_VertexCount; c++)
                    {
                        colorData[c] = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
                    }
                }
                #endregion
                glControl1.Visible = true;
                #region Texture (AssetStudio 2)
                texCoordData = null;
                if (Properties.Settings.Default.previewMeshTexture && m_Mesh.m_UV0 != null && m_Mesh.m_UV0.Length >= m_Mesh.m_VertexCount * 2)
                {
                    texCoordData = new Vector2[m_Mesh.m_VertexCount];
                    for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                    {
                        texCoordData[v] = new Vector2(m_Mesh.m_UV0[v * 2], m_Mesh.m_UV0[v * 2 + 1]);
                    }
                }
                UploadMeshPreviewTextures(m_Mesh);
                #endregion
                CreateVAO();
                StatusStripUpdate("Using OpenGL Version: " + GL.GetString(StringName.Version) + "\n"
                                  + "'Mouse Left'=Rotate | 'Mouse Right'=Move | 'Mouse Wheel'=Zoom \n"
                                  + "'Ctrl W'=Wireframe | 'Ctrl S'=Shade | 'Ctrl N'=ReNormal ");
            }
            else
            {
                StatusStripUpdate("Unable to preview this mesh");
            }
        }

        private void PreviewSprite(AssetItem assetItem, Sprite m_Sprite)
        {
            var image = m_Sprite.GetImage();
            if (image != null)
            {
                var bitmap = new DirectBitmap(image.ConvertToBytes(), image.Width, image.Height);
                image.Dispose();
                assetItem.InfoText = $"Width: {bitmap.Width}\nHeight: {bitmap.Height}\n";
                PreviewTexture(bitmap);
            }
            else
            {
                StatusStripUpdate("Unsupported sprite for preview.");
            }
        }

        private void PreviewTexture(DirectBitmap bitmap)
        {
            imageTexture?.Dispose();
            imageTexture = bitmap;
            previewPanel.BackgroundImage = imageTexture.Bitmap;
            if (imageTexture.Width > previewPanel.Width || imageTexture.Height > previewPanel.Height)
                previewPanel.BackgroundImageLayout = ImageLayout.Zoom;
            else
                previewPanel.BackgroundImageLayout = ImageLayout.Center;
        }

        private void PreviewText(string text)
        {
            textPreviewBox.Text = text;
            textPreviewBox.Visible = true;
        }

        private void SetProgressBarValue(int value)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => { progressBar1.Value = value; }));
            }
            else
            {
                progressBar1.Value = value;
            }
        }

        private void StatusStripUpdate(string statusText)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => { toolStripStatusLabel1.Text = statusText; }));
            }
            else
            {
                toolStripStatusLabel1.Text = statusText;
            }
        }

        private void ResetForm()
        {
            Text = $"AssetStudio 2 {AppDisplayVersion}";
            assetsManager.Clear();
            assemblyLoader.Clear();
            ResetAssemblyAutoLoad();
            exportableAssets.Clear();
            visibleAssets.Clear();
            sceneTreeView.Nodes.Clear();
            assetListView.VirtualListSize = 0;
            assetListView.Items.Clear();
            classesListView.Items.Clear();
            classesListView.Groups.Clear();
            previewPanel.BackgroundImage = Properties.Resources.preview;
            imageTexture?.Dispose();
            imageTexture = null;
            previewPanel.BackgroundImageLayout = ImageLayout.Center;
            assetInfoLabel.Visible = false;
            assetInfoLabel.Text = null;
            textPreviewBox.Visible = false;
            fontPreviewBox.Visible = false;
            glControl1.Visible = false;
            if (meshPartsPanel != null) meshPartsPanel.Visible = false;
            StopAnimationPreview();
            currentCombinedParts = null;
            currentCombinedRootGameObject = null;
            hiddenPartPathIDs.Clear();
            lastSelectedItem = null;
            sortColumn = -1;
            reverseSort = false;
            enableFiltering = false;
            listSearch.Text = " Filter ";

            var count = filterTypeToolStripMenuItem.DropDownItems.Count;
            for (var i = 1; i < count; i++)
            {
                filterTypeToolStripMenuItem.DropDownItems.RemoveAt(1);
            }

            FMODreset();
        }

        private void assetListView_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && assetListView.SelectedIndices.Count > 0)
            {
                goToSceneHierarchyToolStripMenuItem.Visible = false;
                showOriginalFileToolStripMenuItem.Visible = false;
                exportAnimatorwithselectedAnimationClipMenuItem.Visible = false;

                if (assetListView.SelectedIndices.Count == 1)
                {
                    goToSceneHierarchyToolStripMenuItem.Visible = true;
                    showOriginalFileToolStripMenuItem.Visible = true;
                }
                if (assetListView.SelectedIndices.Count >= 1)
                {
                    var selectedAssets = GetSelectedAssets();
                    if (selectedAssets.Any(x => x.Type == ClassIDType.Animator) && selectedAssets.Any(x => x.Type == ClassIDType.AnimationClip))
                    {
                        exportAnimatorwithselectedAnimationClipMenuItem.Visible = true;
                    }
                }

                tempClipboard = assetListView.HitTest(new Point(e.X, e.Y)).SubItem.Text;
                contextMenuStrip1.Show(assetListView, e.X, e.Y);
            }
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Clipboard.SetDataObject(tempClipboard);
        }

        private void exportSelectedAssetsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.Selected, ExportType.Convert);
        }

        private void showOriginalFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectasset = (AssetItem)assetListView.Items[assetListView.SelectedIndices[0]];
            var args = $"/select, \"{selectasset.SourceFile.originalPath ?? selectasset.SourceFile.fullName}\"";
            var pfi = new ProcessStartInfo("explorer.exe", args);
            Process.Start(pfi);
        }

        private void exportAnimatorwithAnimationClipMenuItem_Click(object sender, EventArgs e)
        {
            AssetItem animator = null;
            List<AssetItem> animationList = new List<AssetItem>();
            var selectedAssets = GetSelectedAssets();
            foreach (var assetPreloadData in selectedAssets)
            {
                if (assetPreloadData.Type == ClassIDType.Animator)
                {
                    animator = assetPreloadData;
                }
                else if (assetPreloadData.Type == ClassIDType.AnimationClip)
                {
                    animationList.Add(assetPreloadData);
                }
            }

            if (animator != null)
            {
                var saveFolderDialog = new OpenFolderDialog();
                saveFolderDialog.InitialFolder = saveDirectoryBackup;
                if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    saveDirectoryBackup = saveFolderDialog.Folder;
                    var exportPath = Path.Combine(saveFolderDialog.Folder, "Animator") + Path.DirectorySeparatorChar;
                    ExportAnimatorWithAnimationClip(animator, animationList, exportPath);
                }
            }
        }

        private void exportSelectedObjectsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExportObjects(false);
        }

        private void exportObjectswithAnimationClipMenuItem_Click(object sender, EventArgs e)
        {
            ExportObjects(true);
        }

        private void ExportObjects(bool animation)
        {
            if (sceneTreeView.Nodes.Count > 0)
            {
                var saveFolderDialog = new OpenFolderDialog();
                saveFolderDialog.InitialFolder = saveDirectoryBackup;
                if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    saveDirectoryBackup = saveFolderDialog.Folder;
                    var exportPath = Path.Combine(saveFolderDialog.Folder, "GameObject") + Path.DirectorySeparatorChar;
                    List<AssetItem> animationList = null;
                    if (animation)
                    {
                        animationList = GetSelectedAssets().Where(x => x.Type == ClassIDType.AnimationClip).ToList();
                        if (animationList.Count == 0)
                        {
                            animationList = null;
                        }
                    }
                    ExportObjectsWithAnimationClip(exportPath, sceneTreeView.Nodes, animationList);
                }
            }
            else
            {
                StatusStripUpdate("No Objects available for export");
            }
        }

        private void exportSelectedObjectsmergeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExportMergeObjects(false);
        }

        private void exportSelectedObjectsmergeWithAnimationClipToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExportMergeObjects(true);
        }

        private void ExportMergeObjects(bool animation)
        {
            if (sceneTreeView.Nodes.Count > 0)
            {
                var gameObjects = new List<GameObject>();
                GetSelectedParentNode(sceneTreeView.Nodes, gameObjects);
                if (gameObjects.Count > 0)
                {
                    var saveFileDialog = new SaveFileDialog();
                    saveFileDialog.FileName = gameObjects[0].m_Name + " (merge).fbx";
                    saveFileDialog.AddExtension = false;
                    saveFileDialog.Filter = "Fbx file (*.fbx)|*.fbx";
                    saveFileDialog.InitialDirectory = saveDirectoryBackup;
                    if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        saveDirectoryBackup = Path.GetDirectoryName(saveFileDialog.FileName);
                        var exportPath = saveFileDialog.FileName;
                        List<AssetItem> animationList = null;
                        if (animation)
                        {
                            animationList = GetSelectedAssets().Where(x => x.Type == ClassIDType.AnimationClip).ToList();
                            if (animationList.Count == 0)
                            {
                                animationList = null;
                            }
                        }
                        ExportObjectsMergeWithAnimationClip(exportPath, gameObjects, animationList);
                    }
                }
                else
                {
                    StatusStripUpdate("No Object selected for export.");
                }
            }
        }

        private void goToSceneHierarchyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectasset = (AssetItem)assetListView.Items[assetListView.SelectedIndices[0]];
            if (selectasset.TreeNode != null)
            {
                sceneTreeView.SelectedNode = selectasset.TreeNode;
                tabControl1.SelectedTab = tabPage1;
            }
        }

        private void exportAllAssetsMenuItem_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.All, ExportType.Convert);
        }

        private void exportSelectedAssetsMenuItem_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.Selected, ExportType.Convert);
        }

        private void exportFilteredAssetsMenuItem_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.Filtered, ExportType.Convert);
        }

        private void toolStripMenuItem4_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.All, ExportType.Raw);
        }

        private void toolStripMenuItem5_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.Selected, ExportType.Raw);
        }

        private void toolStripMenuItem6_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.Filtered, ExportType.Raw);
        }

        private void toolStripMenuItem7_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.All, ExportType.Dump);
        }

        private void toolStripMenuItem8_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.Selected, ExportType.Dump);
        }

        private void toolStripMenuItem9_Click(object sender, EventArgs e)
        {
            ExportAssets(ExportFilter.Filtered, ExportType.Dump);
        }

        private void toolStripMenuItem11_Click(object sender, EventArgs e)
        {
            ExportAssetsList(ExportFilter.All);
        }

        private void toolStripMenuItem12_Click(object sender, EventArgs e)
        {
            ExportAssetsList(ExportFilter.Selected);
        }

        private void toolStripMenuItem13_Click(object sender, EventArgs e)
        {
            ExportAssetsList(ExportFilter.Filtered);
        }

        private void exportAllObjectssplitToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (sceneTreeView.Nodes.Count > 0)
            {
                var saveFolderDialog = new OpenFolderDialog();
                saveFolderDialog.InitialFolder = saveDirectoryBackup;
                if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    saveDirectoryBackup = saveFolderDialog.Folder;
                    var savePath = saveFolderDialog.Folder + Path.DirectorySeparatorChar;
                    ExportSplitObjects(savePath, sceneTreeView.Nodes);
                }
            }
            else
            {
                StatusStripUpdate("No Objects available for export");
            }
        }

        private List<AssetItem> GetSelectedAssets()
        {
            var selectedAssets = new List<AssetItem>(assetListView.SelectedIndices.Count);
            foreach (int index in assetListView.SelectedIndices)
            {
                selectedAssets.Add((AssetItem)assetListView.Items[index]);
            }

            return selectedAssets;
        }

        private void FilterAssetList()
        {
            assetListView.BeginUpdate();
            assetListView.SelectedIndices.Clear();
            var show = new List<ClassIDType>();
            if (!allToolStripMenuItem.Checked)
            {
                for (var i = 1; i < filterTypeToolStripMenuItem.DropDownItems.Count; i++)
                {
                    var item = (ToolStripMenuItem)filterTypeToolStripMenuItem.DropDownItems[i];
                    if (item.Checked)
                    {
                        show.Add((ClassIDType)Enum.Parse(typeof(ClassIDType), item.Text));
                    }
                }
                visibleAssets = exportableAssets.FindAll(x => show.Contains(x.Type));
            }
            else
            {
                visibleAssets = exportableAssets;
            }
            if (listSearch.Text != " Filter ")
            {
                visibleAssets = visibleAssets.FindAll(
                    x => x.Text.IndexOf(listSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.SubItems[1].Text.IndexOf(listSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.SubItems[3].Text.IndexOf(listSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            assetListView.VirtualListSize = visibleAssets.Count;
            assetListView.EndUpdate();
        }

        private void ExportAssets(ExportFilter type, ExportType exportType)
        {
            if (exportableAssets.Count > 0)
            {
                var saveFolderDialog = new OpenFolderDialog();
                saveFolderDialog.InitialFolder = saveDirectoryBackup;
                if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    timer.Stop();
                    saveDirectoryBackup = saveFolderDialog.Folder;
                    List<AssetItem> toExportAssets = null;
                    switch (type)
                    {
                        case ExportFilter.All:
                            toExportAssets = exportableAssets;
                            break;
                        case ExportFilter.Selected:
                            toExportAssets = GetSelectedAssets();
                            break;
                        case ExportFilter.Filtered:
                            toExportAssets = visibleAssets;
                            break;
                    }
                    Studio.ExportAssets(saveFolderDialog.Folder, toExportAssets, exportType);
                }
            }
            else
            {
                StatusStripUpdate("No exportable assets loaded");
            }
        }

        private void ExportAssetsList(ExportFilter type)
        {
            // XXX: Only exporting as XML for now, but would JSON(/CSV/other) be useful too?

            if (exportableAssets.Count > 0)
            {
                var saveFolderDialog = new OpenFolderDialog();
                saveFolderDialog.InitialFolder = saveDirectoryBackup;
                if (saveFolderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    timer.Stop();
                    saveDirectoryBackup = saveFolderDialog.Folder;
                    List<AssetItem> toExportAssets = null;
                    switch (type)
                    {
                        case ExportFilter.All:
                            toExportAssets = exportableAssets;
                            break;
                        case ExportFilter.Selected:
                            toExportAssets = GetSelectedAssets();
                            break;
                        case ExportFilter.Filtered:
                            toExportAssets = visibleAssets;
                            break;
                    }
                    Studio.ExportAssetsList(saveFolderDialog.Folder, toExportAssets, ExportListType.XML);
                }
            }
            else
            {
                StatusStripUpdate("No exportable assets loaded");
            }
        }

        #region FMOD
        private void FMODinit()
        {
            FMODreset();

            var result = FMOD.Factory.System_Create(out system);
            if (ERRCHECK(result)) { return; }

            result = system.getVersion(out var version);
            ERRCHECK(result);
            if (version < FMOD.VERSION.number)
            {
                MessageBox.Show($"Error!  You are using an old version of FMOD {version:X}.  This program requires {FMOD.VERSION.number:X}.");
                Application.Exit();
            }

            result = system.init(2, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
            if (ERRCHECK(result)) { return; }

            result = system.getMasterSoundGroup(out masterSoundGroup);
            if (ERRCHECK(result)) { return; }

            result = masterSoundGroup.setVolume(FMODVolume);
            if (ERRCHECK(result)) { return; }
        }

        private void FMODreset()
        {
            timer.Stop();
            FMODprogressBar.Value = 0;
            FMODtimerLabel.Text = "0:00.0 / 0:00.0";
            FMODstatusLabel.Text = "Stopped";
            FMODinfoLabel.Text = "";

            if (sound != null && sound.isValid())
            {
                var result = sound.release();
                ERRCHECK(result);
                sound = null;
            }

            //AssetStudio 2 - Feature 5: clear the waveform/speed state along with everything else.
            FMODwaveformMin = null;
            FMODwaveformMax = null;
            FMODbaseFrequency = 0;
            FMODSpeed = 1.0f;
            FMODwaveformBox?.Invalidate();
        }

        private void FMODplayButton_Click(object sender, EventArgs e)
        {
            if (sound != null && channel != null)
            {
                timer.Start();
                var result = channel.isPlaying(out var playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    if (ERRCHECK(result)) { return; }
                }

                if (playing)
                {
                    result = channel.stop();
                    if (ERRCHECK(result)) { return; }

                    result = system.playSound(sound, null, false, out channel);
                    if (ERRCHECK(result)) { return; }
                    ApplyFMODSpeed();

                    FMODpauseButton.Text = "Pause";
                }
                else
                {
                    result = system.playSound(sound, null, false, out channel);
                    if (ERRCHECK(result)) { return; }
                    ApplyFMODSpeed();
                    FMODstatusLabel.Text = "Playing";

                    if (FMODprogressBar.Value > 0)
                    {
                        uint newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;

                        result = channel.setPosition(newms, FMOD.TIMEUNIT.MS);
                        if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                        {
                            if (ERRCHECK(result)) { return; }
                        }

                    }
                }
            }
        }

        private void FMODpauseButton_Click(object sender, EventArgs e)
        {
            if (sound != null && channel != null)
            {
                var result = channel.isPlaying(out var playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    if (ERRCHECK(result)) { return; }
                }

                if (playing)
                {
                    result = channel.getPaused(out var paused);
                    if (ERRCHECK(result)) { return; }
                    result = channel.setPaused(!paused);
                    if (ERRCHECK(result)) { return; }

                    if (paused)
                    {
                        FMODstatusLabel.Text = "Playing";
                        FMODpauseButton.Text = "Pause";
                        timer.Start();
                    }
                    else
                    {
                        FMODstatusLabel.Text = "Paused";
                        FMODpauseButton.Text = "Resume";
                        timer.Stop();
                    }
                }
            }
        }

        private void FMODstopButton_Click(object sender, EventArgs e)
        {
            if (channel != null)
            {
                var result = channel.isPlaying(out var playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    if (ERRCHECK(result)) { return; }
                }

                if (playing)
                {
                    result = channel.stop();
                    if (ERRCHECK(result)) { return; }
                    //channel = null;
                    //don't FMODreset, it will nullify the sound
                    timer.Stop();
                    FMODprogressBar.Value = 0;
                    FMODtimerLabel.Text = "0:00.0 / 0:00.0";
                    FMODstatusLabel.Text = "Stopped";
                    FMODpauseButton.Text = "Pause";
                }
            }
        }

        private void FMODloopButton_CheckedChanged(object sender, EventArgs e)
        {
            FMOD.RESULT result;

            loopMode = FMODloopButton.Checked ? FMOD.MODE.LOOP_NORMAL : FMOD.MODE.LOOP_OFF;

            if (sound != null)
            {
                result = sound.setMode(loopMode);
                if (ERRCHECK(result)) { return; }
            }

            if (channel != null)
            {
                result = channel.isPlaying(out var playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    if (ERRCHECK(result)) { return; }
                }

                result = channel.getPaused(out var paused);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    if (ERRCHECK(result)) { return; }
                }

                if (playing || paused)
                {
                    result = channel.setMode(loopMode);
                    if (ERRCHECK(result)) { return; }
                }
            }
        }

        private void FMODvolumeBar_ValueChanged(object sender, EventArgs e)
        {
            FMODVolume = Convert.ToSingle(FMODvolumeBar.Value) / 10;

            var result = masterSoundGroup.setVolume(FMODVolume);
            if (ERRCHECK(result)) { return; }
        }

        private void FMODprogressBar_Scroll(object sender, EventArgs e)
        {
            if (channel != null)
            {
                uint newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;
                FMODtimerLabel.Text = $"{newms / 1000 / 60}:{newms / 1000 % 60}.{newms / 10 % 100}/{FMODlenms / 1000 / 60}:{FMODlenms / 1000 % 60}.{FMODlenms / 10 % 100}";
            }
        }

        private void FMODprogressBar_MouseDown(object sender, MouseEventArgs e)
        {
            timer.Stop();
        }

        private void FMODprogressBar_MouseUp(object sender, MouseEventArgs e)
        {
            if (channel != null)
            {
                uint newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;

                var result = channel.setPosition(newms, FMOD.TIMEUNIT.MS);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    if (ERRCHECK(result)) { return; }
                }


                result = channel.isPlaying(out var playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    if (ERRCHECK(result)) { return; }
                }

                if (playing) { timer.Start(); }
            }
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            uint ms = 0;
            bool playing = false;
            bool paused = false;

            if (channel != null)
            {
                var result = channel.getPosition(out ms, FMOD.TIMEUNIT.MS);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                result = channel.isPlaying(out playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                result = channel.getPaused(out paused);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }
            }

            FMODtimerLabel.Text = $"{ms / 1000 / 60}:{ms / 1000 % 60}.{ms / 10 % 100} / {FMODlenms / 1000 / 60}:{FMODlenms / 1000 % 60}.{FMODlenms / 10 % 100}";
            FMODprogressBar.Value = (int)(ms * 1000 / FMODlenms);
            FMODstatusLabel.Text = paused ? "Paused " : playing ? "Playing" : "Stopped";
            FMODwaveformBox?.Invalidate(); // keep the playhead line in sync

            if (system != null && channel != null)
            {
                system.update();
            }
        }

        private bool ERRCHECK(FMOD.RESULT result)
        {
            if (result != FMOD.RESULT.OK)
            {
                FMODreset();
                StatusStripUpdate($"FMOD error! {result} - {FMOD.Error.String(result)}");
                return true;
            }
            return false;
        }
        #endregion

        #region FMOD waveform + speed/pitch (AssetStudio 2 - Feature 5)
        // Built entirely in code and docked into FMODpanel, following the same pattern as
        // InitAnimationControlsPanel/InitMeshPartsPanel, so the Designer-generated layout
        // doesn't need to be touched.
        private void InitFMODWaveformControls()
        {
            FMODwaveformBox = new PictureBox
            {
                BackColor = System.Drawing.Color.FromArgb(24, 24, 24),
                Location = new Point(213, 160),
                Size = new Size(350, 60),
                Cursor = Cursors.Hand
            };
            FMODwaveformBox.Paint += FMODwaveformBox_Paint;
            FMODwaveformBox.MouseDown += FMODwaveformBox_MouseDown;
            FMODwaveformBox.MouseMove += FMODwaveformBox_MouseMove;
            FMODwaveformBox.MouseUp += FMODwaveformBox_MouseUp;
            FMODpanel.Controls.Add(FMODwaveformBox);
            FMODwaveformBox.BringToFront();

            var speedLabel = new Label
            {
                Text = "Speed",
                ForeColor = System.Drawing.Color.White,
                AutoSize = true,
                Location = new Point(580, 284)
            };
            FMODpanel.Controls.Add(speedLabel);

            FMODspeedCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 70,
                Location = new Point(625, 280)
            };
            FMODspeedCombo.Items.AddRange(new object[] { "0.5x", "0.75x", "1x", "1.25x", "1.5x", "2x" });
            FMODspeedCombo.SelectedIndex = 2;
            FMODspeedCombo.SelectedIndexChanged += FMODspeedCombo_SelectedIndexChanged;
            FMODpanel.Controls.Add(FMODspeedCombo);
        }

        private void FMODspeedCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            var text = (string)FMODspeedCombo.SelectedItem;
            if (!float.TryParse(text.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                return;
            }
            FMODSpeed = v;
            ApplyFMODSpeed();
        }

        // Changing frequency changes both playback rate and pitch together (like a tape/turntable
        // speed knob), which is the simplest correct way to do this with FMOD's low-level API
        // without pulling in a separate time-stretching DSP.
        private void ApplyFMODSpeed()
        {
            if (channel == null || FMODbaseFrequency == 0)
            {
                return;
            }
            var result = channel.setFrequency(FMODbaseFrequency * FMODSpeed);
            ERRCHECK(result);
        }

        // Decodes the peak envelope of the currently-loaded FMOD sound into a fixed number of
        // min/max buckets for waveform drawing. `sound` here is already a fully decoded in-memory
        // PCM sample (FMOD's default for createSound without CREATECOMPRESSEDSAMPLE/CREATESTREAM),
        // so we can safely lock/read it directly instead of re-decoding the source bytes ourselves.
        private const int WaveformBuckets = 400;

        private void GenerateFMODWaveform(FMOD.Sound targetSound)
        {
            FMODwaveformMin = null;
            FMODwaveformMax = null;
            if (targetSound == null || !targetSound.isValid())
            {
                return;
            }

            var result = targetSound.getFormat(out _, out var format, out var channels, out var bits);
            if (result != FMOD.RESULT.OK || format != FMOD.SOUND_FORMAT.PCM16 || channels < 1)
            {
                return; // only handle the common 16-bit case; anything else just skips the waveform
            }

            result = targetSound.getLength(out var lenBytes, FMOD.TIMEUNIT.PCMBYTES);
            if (result != FMOD.RESULT.OK || lenBytes == 0)
            {
                return;
            }

            result = targetSound.@lock(0, lenBytes, out var ptr1, out var ptr2, out var len1, out var len2);
            if (result != FMOD.RESULT.OK)
            {
                return;
            }

            try
            {
                var sampleCount = (int)(len1 / 2) / channels; // 16-bit samples, interleaved per channel
                if (sampleCount <= 0)
                {
                    return;
                }
                var min = new float[WaveformBuckets];
                var max = new float[WaveformBuckets];
                for (var i = 0; i < WaveformBuckets; i++)
                {
                    min[i] = 0f;
                    max[i] = 0f;
                }

                var bytes = new byte[len1];
                Marshal.Copy(ptr1, bytes, 0, (int)len1);

                var samplesPerBucket = Math.Max(1, sampleCount / WaveformBuckets);
                for (var i = 0; i < sampleCount; i++)
                {
                    var byteOffset = i * channels * 2;
                    if (byteOffset + 1 >= bytes.Length) { break; }
                    short s = (short)(bytes[byteOffset] | (bytes[byteOffset + 1] << 8));
                    var v = s / 32768f;

                    var bucket = Math.Min(WaveformBuckets - 1, i / samplesPerBucket);
                    if (v < min[bucket]) { min[bucket] = v; }
                    if (v > max[bucket]) { max[bucket] = v; }
                }

                FMODwaveformMin = min;
                FMODwaveformMax = max;
            }
            finally
            {
                targetSound.unlock(ptr1, ptr2, len1, len2);
            }
        }

        private void FMODwaveformBox_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var w = FMODwaveformBox.Width;
            var h = FMODwaveformBox.Height;
            var midY = h / 2f;
            g.Clear(FMODwaveformBox.BackColor);

            if (FMODwaveformMin == null || FMODwaveformMax == null)
            {
                using (var emptyBrush = new SolidBrush(System.Drawing.Color.Gray))
                {
                    g.DrawString("No waveform available", DefaultFont, emptyBrush, 6, midY - 6);
                }
                return;
            }

            using (var waveBrush = new SolidBrush(System.Drawing.Color.FromArgb(90, 170, 250)))
            using (var playPen = new Pen(System.Drawing.Color.White, 1.5f))
            {
                var barWidth = Math.Max(1f, (float)w / WaveformBuckets);
                for (var i = 0; i < WaveformBuckets; i++)
                {
                    var x = i * barWidth;
                    var yTop = midY - FMODwaveformMax[i] * midY;
                    var yBot = midY - FMODwaveformMin[i] * midY;
                    g.FillRectangle(waveBrush, x, yTop, Math.Max(1f, barWidth - 0.5f), Math.Max(1f, yBot - yTop));
                }

                if (FMODlenms > 0)
                {
                    var ratio = FMODprogressBar.Value / 1000f;
                    var playX = ratio * w;
                    g.DrawLine(playPen, playX, 0, playX, h);
                }
            }
        }

        private void FMODwaveformBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (channel == null || FMODlenms == 0)
            {
                return;
            }
            FMODwaveformScrubbing = true;
            timer.Stop();
            SeekFMODToRatio(e.X / (float)FMODwaveformBox.Width);
        }

        private void FMODwaveformBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!FMODwaveformScrubbing)
            {
                return;
            }
            SeekFMODToRatio(e.X / (float)FMODwaveformBox.Width);
        }

        private void FMODwaveformBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!FMODwaveformScrubbing)
            {
                return;
            }
            FMODwaveformScrubbing = false;
            if (channel != null)
            {
                var result = channel.isPlaying(out var playing);
                if (result == FMOD.RESULT.OK && playing) { timer.Start(); }
            }
        }

        private void SeekFMODToRatio(float ratio)
        {
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            FMODprogressBar.Value = (int)(ratio * 1000);
            var newms = (uint)(FMODlenms * ratio);
            FMODtimerLabel.Text = $"{newms / 1000 / 60}:{newms / 1000 % 60}.{newms / 10 % 100}/{FMODlenms / 1000 / 60}:{FMODlenms / 1000 % 60}.{FMODlenms / 10 % 100}";
            if (channel != null)
            {
                var result = channel.setPosition(newms, FMOD.TIMEUNIT.MS);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }
            }
            FMODwaveformBox?.Invalidate();
        }
        #endregion

        #region GLControl
        private void InitOpenTK()
        {
            ChangeGLSize(glControl1.Size);
            GL.ClearColor(System.Drawing.Color.CadetBlue);
            InitFreeCamTimer();
            //AssetStudio 2 fix: force identical attribute locations across every shader program
            //*before* linking. GLSL compilers are free to assign attribute location indices
            //however they like on a per-program basis, so without this, "vertexPosition" (etc.)
            //can end up at a different location in pgmTexID than in pgmID/pgmColorID/pgmBlackID.
            //Since CreateVAO() binds each vertex buffer once to a single location and that VAO
            //is then reused across all four programs at draw time, a mismatch silently feeds
            //position/normal/color data into the wrong attribute slot whenever pgmTexID is the
            //active program - producing a mesh that renders fine untextured but garbled/wrong
            //once texturing is enabled, even though the decoded texture and UVs are correct.
            const int locVertexPosition = 0;
            const int locNormalDirection = 1;
            const int locVertexColor = 2;
            const int locVertexTexCoord = 3;

            pgmID = GL.CreateProgram();
            LoadShader("vs", ShaderType.VertexShader, pgmID, out int vsID);
            LoadShader("fs", ShaderType.FragmentShader, pgmID, out int fsID);
            GL.BindAttribLocation(pgmID, locVertexPosition, "vertexPosition");
            GL.BindAttribLocation(pgmID, locNormalDirection, "normalDirection");
            GL.BindAttribLocation(pgmID, locVertexColor, "vertexColor");
            GL.LinkProgram(pgmID);

            pgmColorID = GL.CreateProgram();
            LoadShader("vs", ShaderType.VertexShader, pgmColorID, out vsID);
            LoadShader("fsColor", ShaderType.FragmentShader, pgmColorID, out fsID);
            GL.BindAttribLocation(pgmColorID, locVertexPosition, "vertexPosition");
            GL.BindAttribLocation(pgmColorID, locNormalDirection, "normalDirection");
            GL.BindAttribLocation(pgmColorID, locVertexColor, "vertexColor");
            GL.LinkProgram(pgmColorID);

            pgmBlackID = GL.CreateProgram();
            LoadShader("vs", ShaderType.VertexShader, pgmBlackID, out vsID);
            LoadShader("fsBlack", ShaderType.FragmentShader, pgmBlackID, out fsID);
            GL.BindAttribLocation(pgmBlackID, locVertexPosition, "vertexPosition");
            GL.BindAttribLocation(pgmBlackID, locNormalDirection, "normalDirection");
            GL.BindAttribLocation(pgmBlackID, locVertexColor, "vertexColor");
            GL.LinkProgram(pgmBlackID);

            pgmTexID = GL.CreateProgram();
            LoadShader("vsTex", ShaderType.VertexShader, pgmTexID, out vsID);
            LoadShader("fsTex", ShaderType.FragmentShader, pgmTexID, out fsID);
            GL.BindAttribLocation(pgmTexID, locVertexPosition, "vertexPosition");
            GL.BindAttribLocation(pgmTexID, locNormalDirection, "normalDirection");
            GL.BindAttribLocation(pgmTexID, locVertexColor, "vertexColor");
            GL.BindAttribLocation(pgmTexID, locVertexTexCoord, "vertexTexCoord");
            GL.LinkProgram(pgmTexID);

            attributeVertexPosition = GL.GetAttribLocation(pgmID, "vertexPosition");
            attributeNormalDirection = GL.GetAttribLocation(pgmID, "normalDirection");
            attributeVertexColor = GL.GetAttribLocation(pgmColorID, "vertexColor");
            attributeVertexTexCoord = GL.GetAttribLocation(pgmTexID, "vertexTexCoord");
            uniformModelMatrix = GL.GetUniformLocation(pgmID, "modelMatrix");
            uniformViewMatrix = GL.GetUniformLocation(pgmID, "viewMatrix");
            uniformProjMatrix = GL.GetUniformLocation(pgmID, "projMatrix");
            uniformModelMatrixTex = GL.GetUniformLocation(pgmTexID, "modelMatrix");
            uniformViewMatrixTex = GL.GetUniformLocation(pgmTexID, "viewMatrix");
            uniformProjMatrixTex = GL.GetUniformLocation(pgmTexID, "projMatrix");
        }

        private static void LoadShader(string filename, ShaderType type, int program, out int address)
        {
            address = GL.CreateShader(type);
            var str = (string)Properties.Resources.ResourceManager.GetObject(filename);
            GL.ShaderSource(address, str);
            GL.CompileShader(address);
            GL.AttachShader(program, address);
            GL.DeleteShader(address);
        }

        private static void CreateVBO(out int vboAddress, Vector3[] data, int address)
        {
            GL.GenBuffers(1, out vboAddress);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
            GL.BufferData(BufferTarget.ArrayBuffer,
                                    (IntPtr)(data.Length * Vector3.SizeInBytes),
                                    data,
                                    BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(address, 3, VertexAttribPointerType.Float, false, 0, 0);
            GL.EnableVertexAttribArray(address);
        }

        private static void CreateVBO(out int vboAddress, Vector4[] data, int address)
        {
            GL.GenBuffers(1, out vboAddress);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
            GL.BufferData(BufferTarget.ArrayBuffer,
                                    (IntPtr)(data.Length * Vector4.SizeInBytes),
                                    data,
                                    BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(address, 4, VertexAttribPointerType.Float, false, 0, 0);
            GL.EnableVertexAttribArray(address);
        }

        //AssetStudio 2: UV coordinates for the textured mesh preview.
        private static void CreateVBO(out int vboAddress, Vector2[] data, int address)
        {
            GL.GenBuffers(1, out vboAddress);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
            GL.BufferData(BufferTarget.ArrayBuffer,
                                    (IntPtr)(data.Length * Vector2.SizeInBytes),
                                    data,
                                    BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(address, 2, VertexAttribPointerType.Float, false, 0, 0);
            GL.EnableVertexAttribArray(address);
        }

        //AssetStudio 2: uploads one GL texture PER SUBMESH for the mesh preview, matching how
        //the exporter walks m_SubMeshes + Renderer.m_Materials (see ModelConverter.cs). The
        //previous implementation resolved a single "main texture" for the whole mesh via
        //MeshTextureResolver.FindMainTexture, which silently breaks on any mesh with more than
        //one material (submesh index no longer lines up with "the" material), producing wrong/
        //garbled coloring in preview despite the export path (which has full Renderer context
        //and doesn't dedupe materials) working correctly. This method uses
        //MeshTextureResolver.FindOrderedMaterials, which preserves submesh-index alignment and
        //does NOT deduplicate, so submesh i always gets materials[i]'s texture.
        //Frees any textures from a previously previewed mesh first so we don't leak GL textures
        //every time the user clicks a different mesh in the tree.
        private void UploadMeshPreviewTextures(Mesh m_Mesh)
        {
            if (!glControl1.IsHandleCreated)
            {
                glControl1.CreateControl();
            }
            glControl1.MakeCurrent();
            if (!glControlLoaded)
            {
                InitOpenTK();
                glControlLoaded = true;
            }

            foreach (var texId in submeshTextureIds)
            {
                if (texId != -1)
                {
                    GL.DeleteTexture(texId);
                }
            }
            submeshTextureIds.Clear();
            submeshIndexOffsets.Clear();
            submeshIndexCounts.Clear();
            meshPreviewHasAnyTexture = false;

            int offset = 0;
            var materials = Properties.Settings.Default.previewMeshTexture
                ? MeshTextureResolver.FindOrderedMaterials(m_Mesh, assetsManager)
                : new List<Material>();

            for (int i = 0; i < m_Mesh.m_SubMeshes.Length; i++)
            {
                int subCount = (int)m_Mesh.m_SubMeshes[i].indexCount;
                submeshIndexOffsets.Add(offset);
                submeshIndexCounts.Add(subCount);
                offset += subCount;

                Texture2D tex = null;
                if (i < materials.Count)
                {
                    tex = MeshTextureResolver.FindMainTextureForMaterial(materials[i]);
                }

                if (tex == null)
                {
                    submeshTextureIds.Add(-1);
                    continue;
                }

                using (var image = tex.ConvertToImage(false))
                {
                    if (image == null)
                    {
                        submeshTextureIds.Add(-1);
                        continue;
                    }
                    var pixels = new byte[image.Width * image.Height * 4];
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var row = accessor.GetRowSpan(y);
                            for (int x = 0; x < row.Length; x++)
                            {
                                int idx = (y * accessor.Width + x) * 4;
                                pixels[idx] = row[x].B;
                                pixels[idx + 1] = row[x].G;
                                pixels[idx + 2] = row[x].R;
                                pixels[idx + 3] = row[x].A;
                            }
                        }
                    });
                    int glTexId = GL.GenTexture();
                    GL.BindTexture(TextureTarget.Texture2D, glTexId);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0,
                        OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    submeshTextureIds.Add(glTexId);
                    meshPreviewHasAnyTexture = true;
                }
            }
        }

        private static void CreateVBO(out int vboAddress, Matrix4 data, int address)
        {
            GL.GenBuffers(1, out vboAddress);
            GL.UniformMatrix4(address, false, ref data);
        }

        private static void CreateEBO(out int address, int[] data)
        {
            GL.GenBuffers(1, out address);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, address);
            GL.BufferData(BufferTarget.ElementArrayBuffer,
                            (IntPtr)(data.Length * sizeof(int)),
                            data,
                            BufferUsageHint.StaticDraw);
        }

        //AssetStudio 2 fix: previously every mesh selection called CreateVAO() and generated a
        //fresh batch of VBOs/EBO with GL.GenBuffers/GL.GenBuffers without ever deleting the
        //previous mesh's buffers (only the VAO itself was deleted). Browsing through many
        //meshes in a session would steadily leak native GPU buffer memory. We now track and
        //delete the previous mesh's buffers before allocating new ones.
        private readonly List<int> meshPreviewVboIds = new List<int>();

        private void CreateVAO()
        {
            GL.DeleteVertexArray(vao);
            if (meshPreviewVboIds.Count > 0)
            {
                GL.DeleteBuffers(meshPreviewVboIds.Count, meshPreviewVboIds.ToArray());
                meshPreviewVboIds.Clear();
            }
            GL.GenVertexArrays(1, out vao);
            GL.BindVertexArray(vao);
            CreateVBO(out var vboPositions, vertexData, attributeVertexPosition);
            meshPreviewVboIds.Add(vboPositions);
            if (normalMode == 0)
            {
                CreateVBO(out var vboNormals, normal2Data, attributeNormalDirection);
                meshPreviewVboIds.Add(vboNormals);
            }
            else
            {
                if (normalData != null)
                {
                    CreateVBO(out var vboNormals, normalData, attributeNormalDirection);
                    meshPreviewVboIds.Add(vboNormals);
                }
            }
            CreateVBO(out var vboColors, colorData, attributeVertexColor);
            meshPreviewVboIds.Add(vboColors);
            if (meshPreviewHasAnyTexture && texCoordData != null && attributeVertexTexCoord >= 0)
            {
                CreateVBO(out var vboTexCoords, texCoordData, attributeVertexTexCoord);
                meshPreviewVboIds.Add(vboTexCoords);
            }
            CreateVBO(out var vboModelMatrix, modelMatrixData, uniformModelMatrix);
            meshPreviewVboIds.Add(vboModelMatrix);
            CreateVBO(out var vboViewMatrix, viewMatrixData, uniformViewMatrix);
            meshPreviewVboIds.Add(vboViewMatrix);
            CreateVBO(out var vboProjMatrix, projMatrixData, uniformProjMatrix);
            meshPreviewVboIds.Add(vboProjMatrix);
            CreateEBO(out var eboElements, indiceData);
            meshPreviewVboIds.Add(eboElements);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
        }

        private void ChangeGLSize(Size size)
        {
            GL.Viewport(0, 0, size.Width, size.Height);

            if (size.Width <= size.Height)
            {
                float k = 1.0f * size.Width / size.Height;
                projMatrixData = Matrix4.CreateScale(1, k, 1);
            }
            else
            {
                float k = 1.0f * size.Height / size.Width;
                projMatrixData = Matrix4.CreateScale(k, 1, 1);
            }
        }

        private void glControl1_Load(object sender, EventArgs e)
        {
            InitOpenTK();
            glControlLoaded = true;
        }

        private void glControl1_Paint(object sender, PaintEventArgs e)
        {
            glControl1.MakeCurrent();
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.BindVertexArray(vao);
            if (wireFrameMode == 0 || wireFrameMode == 2)
            {
                //AssetStudio 2: draw once per submesh so each submesh can use its own texture
                //(or fall back to untextured shading if it has no material/texture), instead of
                //applying one texture to the whole mesh regardless of submesh boundaries.
                bool texturingEnabled = meshPreviewHasAnyTexture && shadeMode == 0 && Properties.Settings.Default.previewMeshTexture;
                for (int s = 0; s < submeshIndexOffsets.Count; s++)
                {
                    int texId = s < submeshTextureIds.Count ? submeshTextureIds[s] : -1;
                    bool useTex = texturingEnabled && texId != -1;
                    int activeProgram = useTex ? pgmTexID : (shadeMode == 0 ? pgmID : pgmColorID);
                    GL.UseProgram(activeProgram);
                    if (useTex)
                    {
                        GL.ActiveTexture(TextureUnit.Texture0);
                        GL.BindTexture(TextureTarget.Texture2D, texId);
                        GL.Uniform1(GL.GetUniformLocation(pgmTexID, "mainTex"), 0);
                        GL.UniformMatrix4(uniformModelMatrixTex, false, ref modelMatrixData);
                        GL.UniformMatrix4(uniformViewMatrixTex, false, ref viewMatrixData);
                        GL.UniformMatrix4(uniformProjMatrixTex, false, ref projMatrixData);
                    }
                    else
                    {
                        GL.UniformMatrix4(uniformModelMatrix, false, ref modelMatrixData);
                        GL.UniformMatrix4(uniformViewMatrix, false, ref viewMatrixData);
                        GL.UniformMatrix4(uniformProjMatrix, false, ref projMatrixData);
                    }
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                    GL.DrawElements(BeginMode.Triangles, submeshIndexCounts[s], DrawElementsType.UnsignedInt,
                        (IntPtr)(submeshIndexOffsets[s] * sizeof(int)));
                    if (useTex)
                    {
                        GL.BindTexture(TextureTarget.Texture2D, 0);
                    }
                }
            }
            //Wireframe
            if (wireFrameMode == 1 || wireFrameMode == 2)
            {
                GL.Enable(EnableCap.PolygonOffsetLine);
                GL.PolygonOffset(-1, -1);
                GL.UseProgram(pgmBlackID);
                GL.UniformMatrix4(uniformModelMatrix, false, ref modelMatrixData);
                GL.UniformMatrix4(uniformViewMatrix, false, ref viewMatrixData);
                GL.UniformMatrix4(uniformProjMatrix, false, ref projMatrixData);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                GL.DrawElements(BeginMode.Triangles, indiceData.Length, DrawElementsType.UnsignedInt, 0);
                GL.Disable(EnableCap.PolygonOffsetLine);
            }
            GL.BindVertexArray(0);
            GL.Flush();
            glControl1.SwapBuffers();
        }

        private void tabControl2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl2.SelectedIndex == 1 && lastSelectedItem != null)
            {
                dumpTextBox.Text = DumpAsset(lastSelectedItem.Asset);
            }
        }

        private void toolStripMenuItem15_Click(object sender, EventArgs e)
        {
            logger.ShowErrorMessage = toolStripMenuItem15.Checked;
        }

        private void glControl1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (!glControl1.Visible)
            {
                return;
            }
            if (freeCamMode)
            {
                FreeCam_MouseWheel(e);
                return;
            }
            viewMatrixData *= Matrix4.CreateScale(1 + e.Delta / 1000f);
            glControl1.Invalidate();
        }

        private void glControl1_MouseDown(object sender, MouseEventArgs e)
        {
            if (freeCamMode)
            {
                FreeCam_MouseDown(e);
                return;
            }
            mdx = e.X;
            mdy = e.Y;
            if (e.Button == MouseButtons.Left)
            {
                lmdown = true;
            }
            if (e.Button == MouseButtons.Right)
            {
                rmdown = true;
            }
        }

        private void glControl1_MouseMove(object sender, MouseEventArgs e)
        {
            if (freeCamMode)
            {
                FreeCam_MouseMove(e);
                return;
            }
            if (lmdown || rmdown)
            {
                float dx = mdx - e.X;
                float dy = mdy - e.Y;
                mdx = e.X;
                mdy = e.Y;
                if (lmdown)
                {
                    dx *= 0.01f;
                    dy *= 0.01f;
                    viewMatrixData *= Matrix4.CreateRotationX(dy);
                    viewMatrixData *= Matrix4.CreateRotationY(dx);
                }
                if (rmdown)
                {
                    dx *= 0.003f;
                    dy *= 0.003f;
                    viewMatrixData *= Matrix4.CreateTranslation(-dx, dy, 0);
                }
                glControl1.Invalidate();
            }
        }

        private void glControl1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                lmdown = false;
            }
            if (e.Button == MouseButtons.Right)
            {
                rmdown = false;
            }
            if (freeCamMode && e.Button == MouseButtons.Right)
            {
                freeCamLooking = false;
            }
        }

        #region FreeCam
        //AssetStudio 2: Freecam preview mode, modeled on Unity's Scene view "flythrough" camera.
        //Toggle with Ctrl+F while the 3D preview is focused.
        //  - Hold Right Mouse Button to look around AND fly with WASD/QE (exactly like Unity:
        //    WASD/QE do nothing unless RMB is held).
        //  - Shift while flying = sprint (3x), Ctrl while flying = slow/precise (0.3x).
        //  - Mouse wheel while RMB is held adjusts fly speed (Unity does this too).
        //  - Mouse wheel while RMB is NOT held dollies the camera forward/backward, like
        //    scrolling in Unity's Scene view when you're not in flythrough mode.
        //  - Movement eases in/out (accelerates while a key is held, coasts to a stop when
        //    released) instead of snapping to a constant speed, for a smoother, less robotic feel.
        //Existing orbit-camera mouse handlers (glControl1_MouseDown/Move/Up/Wheel) are left
        //untouched for non-freecam use; this region only intercepts input when freeCamMode is true.
        private void InitFreeCamTimer()
        {
            if (freeCamTimer != null)
            {
                return;
            }
            freeCamTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60Hz
            freeCamTimer.Tick += (s, e) => FreeCamTick();
            freeCamLastTick = DateTime.Now;
            freeCamTimer.Start();
        }

        private void ToggleFreeCam()
        {
            if (!glControl1.Visible)
            {
                return;
            }
            freeCamMode = !freeCamMode;
            freeCamKeysDown.Clear();
            freeCamVelocity = Vector3.Zero;
            freeCamLooking = false;
            lmdown = false;
            rmdown = false;
            if (freeCamMode)
            {
                //Start the freecam where the orbit camera currently is, looking at the origin,
                //so switching modes doesn't yank the view around.
                var eye = viewMatrixData.ExtractTranslation();
                freeCamPos = new Vector3(0, 0, 3) - eye; //orbit camera stores an inverse-ish offset; approximate a sane start point
                freeCamYaw = (float)-Math.PI / 2;
                freeCamPitch = 0;
            }
            StatusStripUpdate(freeCamMode
                ? "Freecam ON - hold Right Mouse to look + fly (WASD/QE), Shift sprint, Ctrl precise, wheel = speed/dolly, Ctrl+F to exit"
                : "Freecam OFF");
            glControl1.Invalidate();
        }

        private void FreeCamTick()
        {
            var now = DateTime.Now;
            var dt = (float)(now - freeCamLastTick).TotalSeconds;
            freeCamLastTick = now;
            if (!freeCamMode || !glControl1.Visible || dt <= 0 || dt > 0.25f)
            {
                return;
            }

            var forward = new Vector3(
                (float)(Math.Cos(freeCamPitch) * Math.Cos(freeCamYaw)),
                (float)Math.Sin(freeCamPitch),
                (float)(Math.Cos(freeCamPitch) * Math.Sin(freeCamYaw)));
            forward.Normalize();
            var worldUp = Vector3.UnitY;
            var right = Vector3.Cross(forward, worldUp);
            if (right.LengthSquared > 1e-6f)
            {
                right.Normalize();
            }
            var up = Vector3.Cross(right, forward);

            //Unity's flythrough only responds to WASD/QE while RMB is held; without it the
            //keys are inert (matches Scene view exactly), though held keys still decay smoothly.
            var flying = freeCamLooking;
            var wishDir = Vector3.Zero;
            if (flying)
            {
                if (freeCamKeysDown.Contains(Keys.W)) { wishDir += forward; }
                if (freeCamKeysDown.Contains(Keys.S)) { wishDir -= forward; }
                if (freeCamKeysDown.Contains(Keys.D)) { wishDir += right; }
                if (freeCamKeysDown.Contains(Keys.A)) { wishDir -= right; }
                if (freeCamKeysDown.Contains(Keys.E)) { wishDir += up; }
                if (freeCamKeysDown.Contains(Keys.Q)) { wishDir -= up; }
                if (wishDir.LengthSquared > 1e-6f)
                {
                    wishDir.Normalize();
                }
            }

            var speedMul = freeCamKeysDown.Contains(Keys.ShiftKey) ? 3f
                : freeCamKeysDown.Contains(Keys.ControlKey) ? 0.3f
                : 1f;
            var targetVelocity = wishDir * freeCamSpeed * speedMul;

            //Ease toward the target velocity (accelerate while a key is down) and ease back to
            //zero when nothing is held, instead of snapping speed on/off.
            var lerpRate = (targetVelocity.LengthSquared > freeCamVelocity.LengthSquared ? FreeCamAccel : FreeCamDecel) * dt;
            lerpRate = Math.Max(0f, Math.Min(1f, lerpRate));
            freeCamVelocity += (targetVelocity - freeCamVelocity) * lerpRate;

            var moved = false;
            if (freeCamVelocity.LengthSquared > 1e-8f)
            {
                freeCamPos += freeCamVelocity * dt;
                moved = true;
            }

            if (moved || freeCamLooking)
            {
                viewMatrixData = Matrix4.LookAt(freeCamPos, freeCamPos + forward, worldUp);
                glControl1.Invalidate();
            }
        }

        private void FreeCam_KeyDown(Keys keyCode, bool shift)
        {
            freeCamKeysDown.Add(keyCode);
            if (shift)
            {
                freeCamKeysDown.Add(Keys.ShiftKey);
            }
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                freeCamKeysDown.Add(Keys.ControlKey);
            }
        }

        private void FreeCam_KeyUp(Keys keyCode)
        {
            freeCamKeysDown.Remove(keyCode);
            if ((Control.ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                freeCamKeysDown.Remove(Keys.ShiftKey);
            }
            if ((Control.ModifierKeys & Keys.Control) != Keys.Control)
            {
                freeCamKeysDown.Remove(Keys.ControlKey);
            }
        }

        private void FreeCamFrameOrigin()
        {
            var forward = new Vector3(
                (float)(Math.Cos(freeCamPitch) * Math.Cos(freeCamYaw)),
                (float)Math.Sin(freeCamPitch),
                (float)(Math.Cos(freeCamPitch) * Math.Sin(freeCamYaw)));
            forward.Normalize();
            const float FrameDistance = 3f;
            freeCamPos = -forward * FrameDistance; // back off along the current look dir, origin stays in view
            freeCamVelocity = Vector3.Zero;
            viewMatrixData = Matrix4.LookAt(freeCamPos, freeCamPos + forward, Vector3.UnitY);
            StatusStripUpdate("Freecam: framed origin");
            glControl1.Invalidate();
        }

        private void FreeCam_MouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                freeCamLooking = true;
                mdx = e.X;
                mdy = e.Y;
            }
        }

        private void FreeCam_MouseMove(MouseEventArgs e)
        {
            if (!freeCamLooking)
            {
                return;
            }
            var dx = (e.X - mdx) * 0.005f;
            var dy = (e.Y - mdy) * 0.005f;
            mdx = e.X;
            mdy = e.Y;
            freeCamYaw += dx;
            freeCamPitch = Math.Max(-1.55f, Math.Min(1.55f, freeCamPitch - dy));
        }

        private void FreeCam_MouseWheel(MouseEventArgs e)
        {
            if (freeCamLooking)
            {
                //Flying: wheel adjusts fly speed, same as Unity's Scene view.
                freeCamSpeed = Math.Max(0.1f, freeCamSpeed * (e.Delta > 0 ? 1.15f : 1f / 1.15f));
                StatusStripUpdate($"Freecam speed: {freeCamSpeed:0.00}");
            }
            else
            {
                //Not flying: wheel dollies the camera along its look direction, like scrolling
                //in Unity's Scene view when you're not holding RMB.
                var forward = new Vector3(
                    (float)(Math.Cos(freeCamPitch) * Math.Cos(freeCamYaw)),
                    (float)Math.Sin(freeCamPitch),
                    (float)(Math.Cos(freeCamPitch) * Math.Sin(freeCamYaw)));
                forward.Normalize();
                var dolly = (e.Delta / 120f) * freeCamSpeed * 0.5f;
                freeCamPos += forward * dolly;
                viewMatrixData = Matrix4.LookAt(freeCamPos, freeCamPos + forward, Vector3.UnitY);
                glControl1.Invalidate();
            }
        }
        #endregion

        #region AnimationPreview (AssetStudio 2 - Feature 3)
        // Builds the playback controls overlay purely in code (mirrors InitMeshPartsPanel) and
        // docks it at the bottom of previewPanel, on top of glControl1. Hidden until an
        // AnimationClip/Animator is actually previewed.
        private void InitAnimationControlsPanel()
        {
            animationControlsPanel = new Panel
            {
                BackColor = System.Drawing.Color.FromArgb(235, 32, 32, 32),
                Height = 66,
                Visible = false
            };

            animationPlayPauseButton = new Button
            {
                Text = "\u25B6", // play glyph; swapped to pause glyph while playing
                Width = 36,
                Height = 28,
                Location = new Point(6, 6)
            };
            animationPlayPauseButton.Click += (s, e) => ToggleAnimationPlayback();

            animationTimeLabel = new Label
            {
                Text = "0.00 / 0.00s",
                ForeColor = System.Drawing.Color.White,
                AutoSize = false,
                Width = 110,
                Height = 20,
                Location = new Point(48, 12),
                TextAlign = ContentAlignment.MiddleLeft
            };

            animationLoopCheckBox = new CheckBox
            {
                Text = "Loop",
                ForeColor = System.Drawing.Color.White,
                AutoSize = true,
                Checked = true,
                Location = new Point(162, 10)
            };
            animationLoopCheckBox.CheckedChanged += (s, e) => animationPreviewLoop = animationLoopCheckBox.Checked;

            animationSpeedCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 70,
                Location = new Point(226, 8)
            };
            animationSpeedCombo.Items.AddRange(new object[] { "0.25x", "0.5x", "1x", "1.5x", "2x", "4x" });
            animationSpeedCombo.SelectedIndex = 2;
            animationSpeedCombo.SelectedIndexChanged += (s, e) =>
            {
                var text = (string)animationSpeedCombo.SelectedItem;
                if (float.TryParse(text.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    animationPreviewSpeed = v;
                }
            };

            animationClipSelector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 160,
                Location = new Point(304, 8),
                Visible = false // only shown when an Animator with multiple clips is selected
            };
            animationClipSelector.SelectedIndexChanged += (s, e) =>
            {
                if (suppressAnimationScrubEvent) return;
                if (animationClipSelector.SelectedIndex < 0 || currentAnimationClipCandidates == null) return;
                if (animationClipSelector.SelectedIndex >= currentAnimationClipCandidates.Count) return;
                LoadAnimationClipIntoRig(currentAnimationClipCandidates[animationClipSelector.SelectedIndex]);
            };

            animationScrubBar = new TrackBar
            {
                Minimum = 0,
                Maximum = 1000,
                TickStyle = TickStyle.None,
                Location = new Point(6, 36),
                Height = 28
            };
            animationScrubBar.Scroll += (s, e) =>
            {
                if (currentDecodedClip == null) return;
                animationPreviewPlaying = false;
                UpdatePlayPauseGlyph();
                animationPreviewTime = (animationScrubBar.Value / 1000f) * currentDecodedClip.Length;
                ApplyAnimationFrame();
            };

            animationControlsPanel.Controls.Add(animationPlayPauseButton);
            animationControlsPanel.Controls.Add(animationTimeLabel);
            animationControlsPanel.Controls.Add(animationLoopCheckBox);
            animationControlsPanel.Controls.Add(animationSpeedCombo);
            animationControlsPanel.Controls.Add(animationClipSelector);
            animationControlsPanel.Controls.Add(animationScrubBar);

            previewPanel.Controls.Add(animationControlsPanel);
            animationControlsPanel.BringToFront();
            PositionAnimationControlsPanel();

            animationPreviewTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60Hz
            animationPreviewTimer.Tick += (s, e) => AnimationPreviewTick();
        }

        // Docks the panel to the full width, bottom of the preview area.
        private void PositionAnimationControlsPanel()
        {
            if (animationControlsPanel == null || previewPanel == null) return;
            animationControlsPanel.Width = previewPanel.ClientSize.Width;
            animationControlsPanel.Location = new Point(0, Math.Max(0, previewPanel.ClientSize.Height - animationControlsPanel.Height));
            animationScrubBar.Width = Math.Max(50, animationControlsPanel.Width - 12);
        }

        private void UpdatePlayPauseGlyph()
        {
            if (animationPlayPauseButton == null) return;
            animationPlayPauseButton.Text = animationPreviewPlaying ? "\u23F8" : "\u25B6";
        }

        private void ToggleAnimationPlayback()
        {
            if (currentDecodedClip == null) return;
            animationPreviewPlaying = !animationPreviewPlaying;
            if (animationPreviewPlaying && animationPreviewTime >= currentDecodedClip.Length - 1e-4f)
            {
                animationPreviewTime = 0f; // restart from the beginning if resuming at the end
            }
            animationPreviewLastTick = DateTime.Now;
            UpdatePlayPauseGlyph();
        }

        // Stops playback and hides the controls; called whenever the user selects something
        // that isn't an AnimationClip/Animator, and from ResetForm.
        private void StopAnimationPreview()
        {
            animationPreviewPlaying = false;
            if (animationPreviewTimer != null && animationPreviewTimer.Enabled)
            {
                animationPreviewTimer.Stop();
            }
            if (animationControlsPanel != null)
            {
                animationControlsPanel.Visible = false;
            }
            currentAnimationRig = null;
            currentDecodedClip = null;
            currentAnimationClipCandidates = null;
            animationBoundsFitted = false;
        }

        // Entry point for selecting an Animator directly: builds the rig once and offers every
        // clip its controller references in a dropdown, defaulting to the first one (or to
        // `preselectedClip` if the Animator was reached by selecting one of its clips).
        private void PreviewAnimator(Animator animator, AnimationClip preselectedClip)
        {
            try
            {
                var rig = AnimationPreview.BuildRig(animator);
                if (rig == null || rig.Parts.Count == 0)
                {
                    StatusStripUpdate("No skinned/static meshes found under this Animator's rig.");
                    return;
                }

                var clips = CollectClipsForAnimator(animator);
                if (clips.Count == 0)
                {
                    StatusStripUpdate("This Animator's controller has no AnimationClips to preview.");
                    return;
                }

                currentAnimationRig = rig;
                currentAnimationClipCandidates = clips;

                suppressAnimationScrubEvent = true;
                animationClipSelector.Items.Clear();
                foreach (var c in clips) animationClipSelector.Items.Add(c.m_Name);
                animationClipSelector.Visible = clips.Count > 1;
                var startIndex = preselectedClip != null ? clips.IndexOf(preselectedClip) : 0;
                animationClipSelector.SelectedIndex = Math.Max(0, startIndex);
                suppressAnimationScrubEvent = false;

                LoadAnimationClipIntoRig(clips[Math.Max(0, startIndex)]);
            }
            catch (Exception ex)
            {
                StatusStripUpdate("Failed to preview Animator: " + ex.Message);
            }
        }

        // Entry point for selecting an AnimationClip directly: finds the best rig to play it on
        // (see AnimationPreview.FindOwningAnimator), then defers to PreviewAnimator so the same
        // "pick a clip" dropdown works whichever way the user navigated in.
        private void PreviewAnimationClip(AnimationClip clip)
        {
            try
            {
                var animator = AnimationPreview.FindOwningAnimator(clip, assetsManager);
                if (animator == null)
                {
                    StatusStripUpdate("No rig (Animator) found for this AnimationClip - nothing to preview.");
                    return;
                }
                PreviewAnimator(animator, clip);
            }
            catch (Exception ex)
            {
                StatusStripUpdate("Failed to preview AnimationClip: " + ex.Message);
            }
        }

        private List<AnimationClip> CollectClipsForAnimator(Animator animator)
        {
            var clips = new List<AnimationClip>();
            if (!animator.m_Controller.TryGet(out var rc)) return clips;

            void AddFrom(RuntimeAnimatorController controller)
            {
                switch (controller)
                {
                    case AnimatorController ac:
                        foreach (var pptr in ac.m_AnimationClips)
                        {
                            if (pptr.TryGet(out var c) && !clips.Contains(c)) clips.Add(c);
                        }
                        break;
                    case AnimatorOverrideController aoc:
                        foreach (var clipOverride in aoc.m_Clips)
                        {
                            AnimationClip c = null;
                            if (clipOverride.m_OverrideClip.TryGet(out var oc)) c = oc;
                            else if (clipOverride.m_OriginalClip.TryGet(out var origC)) c = origC;
                            if (c != null && !clips.Contains(c)) clips.Add(c);
                        }
                        if (aoc.m_Controller.TryGet(out var baseRc))
                        {
                            AddFrom(baseRc);
                        }
                        break;
                }
            }
            AddFrom(rc);
            return clips;
        }

        // (Re)decodes `clip` against the already-built currentAnimationRig, resets playback to
        // frame 0, shows the controls panel, and renders the bind/first-frame pose immediately.
        private void LoadAnimationClipIntoRig(AnimationClip clip)
        {
            if (currentAnimationRig == null) return;

            var decoded = AnimationPreview.DecodeClip(clip, currentAnimationRig);
            currentDecodedClip = decoded;
            animationPreviewTime = 0f;
            animationPreviewPlaying = true;
            animationPreviewLastTick = DateTime.Now;
            animationBoundsFitted = false;
            UpdatePlayPauseGlyph();

            animationControlsPanel.Visible = true;
            PositionAnimationControlsPanel();
            animationControlsPanel.BringToFront();
            if (meshPartsPanel != null) meshPartsPanel.Visible = false; // parts overlay doesn't apply here

            viewMatrixData = Matrix4.CreateRotationY(-(float)Math.PI / 4) * Matrix4.CreateRotationX(-(float)Math.PI / 6);

            animationPreviewTimer.Start();
            ApplyAnimationFrame();

            var boneCount = currentAnimationRig.BonesByPath.Count;
            var trackCount = decoded.TracksByPath.Count;
            StatusStripUpdate($"Previewing animation '{decoded.Name}' ({trackCount}/{boneCount} bones animated, {decoded.Length:0.00}s) \n"
                              + "'Space'=Play/Pause | 'Mouse Left'=Rotate | 'Mouse Right'=Move | 'Mouse Wheel'=Zoom | 'Ctrl F'=Freecam");
        }

        private void AnimationPreviewTick()
        {
            if (currentDecodedClip == null || currentAnimationRig == null) return;
            if (!animationPreviewPlaying)
            {
                return;
            }

            var now = DateTime.Now;
            var dt = (float)(now - animationPreviewLastTick).TotalSeconds;
            animationPreviewLastTick = now;
            if (dt <= 0 || dt > 0.25f) return;

            animationPreviewTime += dt * animationPreviewSpeed;
            if (animationPreviewTime >= currentDecodedClip.Length)
            {
                if (animationPreviewLoop)
                {
                    animationPreviewTime %= Math.Max(currentDecodedClip.Length, 1e-4f);
                }
                else
                {
                    animationPreviewTime = currentDecodedClip.Length;
                    animationPreviewPlaying = false;
                    UpdatePlayPauseGlyph();
                }
            }

            ApplyAnimationFrame();
        }

        // Evaluates the pose at animationPreviewTime, re-skins every mesh part in the rig, and
        // pushes the result into the existing GL mesh-preview buffers/VAO so it draws through
        // the same code path as the static mesh preview.
        private void ApplyAnimationFrame()
        {
            if (currentAnimationRig == null || currentDecodedClip == null) return;

            AnimationPreview.EvaluatePose(currentAnimationRig, currentDecodedClip, animationPreviewTime);
            var skinned = AnimationPreview.SkinRig(currentAnimationRig);
            if (skinned.Vertices.Length == 0)
            {
                return;
            }

            vertexData = skinned.Vertices;
            normalData = skinned.Normals;
            normal2Data = skinned.Normals;
            colorData = skinned.Colors;
            indiceData = skinned.Indices;
            texCoordData = null;

            // One "submesh" spanning the whole combined index buffer - animation preview doesn't
            // resolve per-submesh textures (rig can span many meshes/materials), so draw it as a
            // single untextured/shaded batch through the existing per-submesh draw loop.
            submeshTextureIds.Clear();
            submeshIndexOffsets.Clear();
            submeshIndexCounts.Clear();
            meshPreviewHasAnyTexture = false;
            if (indiceData.Length > 0)
            {
                submeshTextureIds.Add(-1);
                submeshIndexOffsets.Add(0);
                submeshIndexCounts.Add(indiceData.Length);
            }

            // Fit the camera to the *bind pose* bounds once per clip load rather than every
            // frame, so the model doesn't rescale/re-center (and appear to "shake") as the
            // animated bounds change tick to tick. Recomputed lazily the first time this clip's
            // buffers are non-empty.
            if (!animationBoundsFitted)
            {
                FitAnimationCamera(skinned.Vertices);
                animationBoundsFitted = true;
            }

            if (!glControl1.Visible)
            {
                glControl1.Visible = true;
            }
            CreateVAO();
            glControl1.Invalidate();

            if (!suppressAnimationScrubEvent)
            {
                suppressAnimationScrubEvent = true;
                var frac = currentDecodedClip.Length > 1e-4f ? animationPreviewTime / currentDecodedClip.Length : 0f;
                animationScrubBar.Value = Math.Max(0, Math.Min(1000, (int)(frac * 1000)));
                suppressAnimationScrubEvent = false;
            }
            animationTimeLabel.Text = $"{animationPreviewTime:0.00} / {currentDecodedClip.Length:0.00}s";
        }

        private void FitAnimationCamera(Vector3[] vertices)
        {
            if (vertices == null || vertices.Length == 0) return;
            float[] min = { vertices[0].X, vertices[0].Y, vertices[0].Z };
            float[] max = { vertices[0].X, vertices[0].Y, vertices[0].Z };
            foreach (var v in vertices)
            {
                min[0] = Math.Min(min[0], v.X); max[0] = Math.Max(max[0], v.X);
                min[1] = Math.Min(min[1], v.Y); max[1] = Math.Max(max[1], v.Y);
                min[2] = Math.Min(min[2], v.Z); max[2] = Math.Max(max[2], v.Z);
            }
            var dist = new Vector3(max[0] - min[0], max[1] - min[1], max[2] - min[2]);
            var offset = new Vector3((max[0] + min[0]) / 2, (max[1] + min[1]) / 2, (max[2] + min[2]) / 2);
            var d = Math.Max(1e-5f, dist.Length);
            modelMatrixData = Matrix4.CreateTranslation(-offset) * Matrix4.CreateScale(2f / d);
        }
        #endregion
        #endregion
    }
}
