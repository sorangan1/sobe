using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Screens.Edit;
using OsuBeatmapEditor.Game.UI;
using OsuBeatmapEditor.Game.Updates;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// Landing screen ("main menu"): an osu!lazer-style beatmap carousel on the right (with search +
    /// sort), the selected map's background filling the screen (dimmed), and bottom-corner actions
    /// for creating a new beatmap and editing the selected one.
    /// </summary>
    public partial class SongSelectScreen : Screen
    {
        private const float carousel_width = 580;

        private BeatmapCarousel carousel = null!;
        private Container rightArea = null!;
        private CarouselSearchTextBox searchBox = null!;
        private Container backgroundContainer = null!;
        private OsuButton newBeatmapButton = null!;
        private MenuIconButton previewToggleButton = null!;
        // Top-bar actions that only make sense when signed in (collabs + friends); swapped for a
        // "sign in" placeholder card when logged out.
        private FillFlowContainer authActions = null!;
        private BeatmapInfoPanel infoPanel = null!;
        private NewBeatmapOverlay newBeatmapOverlay = null!;
        private NewDifficultyOverlay newDifficultyOverlay = null!;
        private ConfirmOverlay confirmOverlay = null!;
        private EditorSettingsOverlay settingsOverlay = null!;
        private CollabsListOverlay collabsOverlay = null!;
        private CollabRevisionsOverlay revisionsOverlay = null!;
        private DownloadMapsOverlay downloadOverlay = null!;
        private UpdateBanner updateBanner = null!;
        private UpdatePromptOverlay updatePrompt = null!;

        // App-wide update mechanism (cached at the game root; absent under the test browser).
        [Resolved(CanBeNull = true)]
        private UpdateManager? updates { get; set; }

        private bool updatesInitialised;

        // Cached so the shared editor-settings overlay (resolved via DI) can be opened from here too.
        private DependencyContainer dependencies = null!;
        private EditorSettings editorSettings = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            dependencies = new DependencyContainer(parent);
            dependencies.CacheAs(editorSettings = new EditorSettings(parent.Get<GameHost>().Storage));
            return dependencies;
        }

        // Cached by the game host; absent under the standalone test browser, so calls are null-guarded.
        [Resolved(CanBeNull = true)]
        private ToastOverlay? toasts { get; set; }

        // Online login session + local collab links (cached at the game root; absent under the test browser).
        [Resolved(CanBeNull = true)]
        private Online.AuthManager? auth { get; set; }

        [Resolved(CanBeNull = true)]
        private Online.CollabSession? collabs { get; set; }

        private Storage storage = null!;
        private GameHost gameHost = null!;

        // The set/difficulty a pending "Create new Difficulty" action is templating from.
        private BeatmapSetModel? pendingDifficultySet;
        private BeatmapDifficultyModel? pendingDifficultyTemplate;

        private IReadOnlyList<BeatmapSetModel> sets = Array.Empty<BeatmapSetModel>();

        private LargeTextureStore? textures;
        private LargeTextureStore? cardTextures;
        private Drawable? currentBackground;
        private int backgroundRequest;

        private ITrackStore? trackStore;
        private PreviewTrack? currentPreview;
        private int previewRequest;

        private BeatmapSetModel? selectedSet;
        private BeatmapDifficultyModel? selectedDiff;
        private string? loadedAudioKey;
        private string? loadedBackgroundKey;
        private EditorScreen? pushedEditor;

        private SongSelectPreferences prefs = null!;
        private ScheduledDelegate? searchDebounce;

        // The game window, kept so the OS file-drop handler can be detached on disposal.
        private IWindow? window;
        private Action<string>? dragDropHandler;

        [BackgroundDependencyLoader]
        private void load(GameHost host, AudioManager audio, Storage storage)
        {
            this.storage = storage;
            gameHost = host;
            prefs = new SongSelectPreferences(storage);

            sets = BeatmapStore.LoadAll();

            string? dataDir = LazerStorage.FindDataDirectory();
            if (dataDir != null)
            {
                var fileStore = new StorageBackedResourceStore(new NativeStorage(Path.Combine(dataDir, "files"), host));
                // Full-resolution store for the screen-filling hero background (it covers the whole window).
                textures = new LargeTextureStore(host.Renderer, host.CreateTextureLoaderStore(fileStore), TextureFilteringMode.Linear, false);
                // Separate store for the carousel cards: those only ever show the background in a small panel, so
                // downscaling the longest side to 640px before upload keeps their combined VRAM a fraction of the
                // full-resolution art - with no visible quality loss at card size. The hero keeps full detail.
                var cardLoader = new DownscalingTextureLoaderStore(host.CreateTextureLoaderStore(fileStore), 640);
                cardTextures = new LargeTextureStore(host.Renderer, cardLoader, TextureFilteringMode.Linear, false);
                trackStore = audio.GetTrackStore(new StorageBackedResourceStore(new NativeStorage(Path.Combine(dataDir, "files"), host)));
            }

            ThemedDropdown<SortMode> sortDropdown;

            // The context-menu container renders the carousel's right-click menus (styled items).
            InternalChild = new PaddedContextMenuContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new Container
                {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = OsuColour.BackgroundDark,
                    },
                    backgroundContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                    // Dim layer keeps the UI readable over bright backgrounds.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = 0.55f,
                    },
                    // Dark gradient behind the carousel strip (transparent on the left, darker toward the
                    // right) so the cards read clearly against the background image.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(
                            new Color4(0f, 0f, 0f, 0f),
                            new Color4(0f, 0f, 0f, 0.6f)),
                    },
                    // Full-screen carousel: click/drag/scroll work anywhere, while the cards themselves
                    // sit in a fixed strip on the right. Sits below the button/toolbar so those stay usable.
                    carousel = new BeatmapCarousel
                    {
                        RelativeSizeAxes = Axes.Both,
                        // Leaves room for the top bar and the search/sort toolbar below it.
                        Padding = new MarginPadding { Top = 120, Bottom = 24 },
                        SelectionChanged = onSelectionChanged,
                    },
                    // Selected-map readout, top-left under the top bar.
                    infoPanel = new BeatmapInfoPanel
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Margin = new MarginPadding { Left = 40, Top = TopBar.HeightPx + 16 },
                    },
                    newBeatmapButton = new OsuButton("New Beatmap", OsuColour.Pink)
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Size = new Vector2(220, 56),
                        Margin = new MarginPadding(30),
                        Action = onNewBeatmap,
                    },
                    // Top-right toolbar (search + sort), drawn above the carousel so the dropdown popup
                    // and the search box stay interactive.
                    rightArea = new Container
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Width = carousel_width - 24,
                        Height = 40,
                        Margin = new MarginPadding { Top = TopBar.HeightPx + 12, Right = 24 },
                        Child = new GridContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 40,
                            ColumnDimensions = new[]
                            {
                                new Dimension(GridSizeMode.Absolute, 22),
                                new Dimension(GridSizeMode.Absolute, 8),
                                new Dimension(),
                                new Dimension(GridSizeMode.Absolute, 12),
                                new Dimension(GridSizeMode.Absolute, 150),
                            },
                            Content = new[]
                            {
                                new Drawable?[]
                                {
                                    new SpriteIcon
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Icon = FontAwesome.Solid.Search,
                                        Size = new Vector2(16),
                                        Colour = OsuColour.TextMuted,
                                    },
                                    null,
                                    searchBox = new CarouselSearchTextBox
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 40,
                                        PlaceholderText = "Search...",
                                    },
                                    null,
                                    sortDropdown = new ThemedDropdown<SortMode>
                                    {
                                        RelativeSizeAxes = Axes.X,
                                    },
                                },
                            },
                        },
                    },
                    // Top chrome bar: app version (centre) + the account card (right). Drawn above the
                    // carousel/background but below the modal overlays.
                    new TopBar
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                    },
                    // Left top-bar actions (over the bar): settings, download, and a grouped card for the
                    // random/refresh/preview-toggle controls.
                    new FillFlowContainer
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                        Margin = new MarginPadding { Left = EditorTheme.Spacing.Lg, Top = (TopBar.HeightPx - 40) / 2f },
                        Children = new Drawable[]
                        {
                            topBarButton(FontAwesome.Solid.Cog, "Settings", () => settingsOverlay.ToggleVisibility()),
                            topBarButton(FontAwesome.Solid.Download, "Download maps", () => downloadOverlay.ToggleVisibility()),
                            buildPlaybackCard(),
                        },
                    },
                    // Right top-bar actions, immediately left of the account card: collabs + friends.
                    // Both are online (sobe) features, so the whole row is hidden while logged out.
                    authActions = new FillFlowContainer
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        AutoSizeAxes = Axes.Both,
                        Alpha = 0,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                        Margin = new MarginPadding { Right = UserProfileCard.CardWidth + EditorTheme.Spacing.Lg, Top = (TopBar.HeightPx - 40) / 2f },
                        Children = new Drawable[]
                        {
                            topBarButton(FontAwesome.Solid.Users, "Collabs", () => collabsOverlay.ToggleVisibility()),
                            topBarButton(FontAwesome.Solid.UserFriends, "Friends (sobe mutuals)",
                                () => toasts?.Push("Friends list coming soon")),
                        },
                    },
                    newBeatmapOverlay = new NewBeatmapOverlay
                    {
                        Created = onBeatmapCreated,
                    },
                    newDifficultyOverlay = new NewDifficultyOverlay
                    {
                        Confirmed = onCreateDifficultyConfirmed,
                    },
                    confirmOverlay = new ConfirmOverlay(),
                    settingsOverlay = new EditorSettingsOverlay(),
                    collabsOverlay = new CollabsListOverlay
                    {
                        Session = collabs,
                        IsLoggedIn = () => auth?.IsLoggedIn == true,
                        Fetch = () => auth?.Token is string tok
                            ? Online.SobeApi.GetMyCollabsAsync(tok)
                            : Task.FromResult(new List<Online.CollabSummary>()),
                        Download = c => auth?.Token is string tok && collabs != null
                            ? Online.CollabSync.DownloadAsync(tok, c, collabs)
                            : Task.FromResult((false, "Not logged in.")),
                        Pull = c => auth?.Token is string tok && collabs != null
                            ? Online.CollabSync.PullAsync(tok, c, collabs)
                            : Task.FromResult((false, "Not logged in.")),
                        FetchInvites = () => auth?.Token is string tok
                            ? Online.SobeApi.GetInvitesAsync(tok)
                            : Task.FromResult(new List<Online.CollabInvite>()),
                        Accept = i => auth?.Token is string tok
                            ? Online.SobeApi.AcceptInviteAsync(tok, i.Id)
                            : Task.FromResult(false),
                        Decline = i => auth?.Token is string tok
                            ? Online.SobeApi.DeclineInviteAsync(tok, i.Id)
                            : Task.FromResult(false),
                        IsDownloaded = collabIsDownloaded,
                        OpenMap = openCollabMap,
                        ShowInfo = showCollabRevisions,
                        OnLibraryChanged = () => reloadBeatmaps(),
                    },
                    revisionsOverlay = new CollabRevisionsOverlay(),
                    downloadOverlay = new DownloadMapsOverlay
                    {
                        OpenUrl = url => gameHost.OpenUrlExternally(url),
                    },
                    // Update notice, bottom-left just above the New Beatmap button.
                    updateBanner = new UpdateBanner
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Margin = new MarginPadding { Left = 30, Bottom = 30 + 56 + 14 },
                    },
                    // One-time "automatic updates?" prompt, on top of everything.
                    updatePrompt = new UpdatePromptOverlay(),
                },
                },
            };

            carousel.EditRequested = openEditor;
            carousel.CreateDifficultyRequested = onCreateDifficulty;
            carousel.CreateSetRequested = onCreateSet;
            carousel.DeleteSetRequested = onDeleteSet;
            carousel.DeleteDifficultyRequested = onDeleteDifficulty;
            carousel.ExportSetRequested = exportSet;
            carousel.Textures = cardTextures;

            sortDropdown.Items = Enum.GetValues<SortMode>();
            // Restore the last-used sort and persist any change.
            sortDropdown.Current.Value = prefs.Sort.Value;
            sortDropdown.Current.BindValueChanged(e =>
            {
                prefs.Sort.Value = e.NewValue;
                carousel.SetSort(e.NewValue);
            });

            // Debounce so a fast typer doesn't trigger a rebuild on every keystroke.
            searchBox.Current.BindValueChanged(e =>
            {
                searchDebounce?.Cancel();
                searchDebounce = Scheduler.AddDelayed(() => carousel.SetFilter(e.NewValue), 60);
            });

            // Show the collab/friends actions only while signed in (and react to login/logout live).
            if (auth != null)
                auth.User.BindValueChanged(_ => updateAuthActions(), true);
            else
                updateAuthActions();

            carousel.SetSort(prefs.Sort.Value);
            carousel.SetBeatmaps(sets);
            // Open on a random map (and start its preview) once the screen is laid out.
            Schedule(carousel.SelectRandom);

            // Dropping an audio file on the window opens the New Beatmap dialog with it preselected.
            // The event fires off the update thread, so marshal back before touching drawables.
            window = host.Window;
            if (window != null)
            {
                dragDropHandler = path => Schedule(() => onFileDropped(path));
                window.DragDrop += dragDropHandler;
            }
        }

        /// <summary>
        /// Handles an OS file drop while this is the active screen: a <c>.osz</c> is imported straight into the
        /// library; a supported audio file opens the New Beatmap dialog. Anything else hints what's accepted.
        /// </summary>
        private void onFileDropped(string path)
        {
            if (!this.IsCurrentScreen())
                return;

            if (!File.Exists(path))
                return;

            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".osz")
            {
                importOsz(path);
                return;
            }

            // .sobemod (Review-layer) files are only meaningful inside the editor; ignore them on song select.
            if (ext == ".sobemod")
                return;

            if (!NewBeatmapOverlay.AudioExtensions.Contains(ext))
            {
                toasts?.Push("Drop an .osz to import, or an mp3/ogg/wav to start a new beatmap", EditorTheme.Colours.Warning);
                return;
            }

            newBeatmapOverlay.ShowForDroppedAudio(path);
        }

        /// <summary>Exports a set to a <c>.osz</c> off-thread, then toasts the result and reveals the file.</summary>
        private void exportSet(BeatmapSetModel set)
        {
            toasts?.Push($"Exporting {set.Artist} - {set.Title}...");
            string exportsDir = storage.GetFullPath("exports");

            Task.Run(() =>
            {
                string? error = BeatmapArchiveExporter.Export(set, exportsDir, out string outputPath);
                Schedule(() =>
                {
                    if (error == null)
                    {
                        toasts?.Push($"Exported to {Path.GetFileName(outputPath)}", EditorTheme.Colours.Success);
                        gameHost.PresentFileExternally(outputPath);
                    }
                    else
                    {
                        toasts?.Push(error, EditorTheme.Colours.Error);
                    }
                });
            });
        }

        /// <summary>Imports a dropped <c>.osz</c> into osu!lazer's realm off-thread, then toasts + refreshes.</summary>
        private void importOsz(string oszPath)
        {
            toasts?.Push($"Importing {Path.GetFileName(oszPath)}...");

            Task.Run(() =>
            {
                var result = BeatmapArchiveImporter.ImportOsz(oszPath);
                Schedule(() =>
                {
                    if (result.Success)
                    {
                        toasts?.Push($"Imported {result.Message}", EditorTheme.Colours.Success);
                        reloadBeatmaps();
                    }
                    else
                    {
                        toasts?.Push(result.Message, EditorTheme.Colours.Error);
                    }
                });
            });
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            initUpdates();

            rightArea.MoveToX(40).FadeOut();
            rightArea.MoveToX(0, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);

            newBeatmapButton.MoveToY(40).FadeOut();
            newBeatmapButton.Delay(150).MoveToY(0, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);

            // Focus the search box so typing immediately filters (it holds focus thereafter).
            Schedule(() => GetContainingFocusManager()?.ChangeFocus(searchBox));
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);
            // Don't let the preview keep playing over the editor's own audio.
            currentPreview?.Stop();
        }

        /// <summary>
        /// Wires up the self-update flow once: kicks off a background version check, shows the one-time
        /// "automatic updates?" prompt on first launch, and auto-downloads when an update appears and the
        /// user has opted into automatic updates. The banner reflects progress and offers the restart.
        /// </summary>
        private void initUpdates()
        {
            if (updatesInitialised || updates == null)
                return;

            updatesInitialised = true;

            updates.CheckForUpdatesOnce();
            updates.State.BindValueChanged(_ => maybeAutoPrepare());

            if (!editorSettings.AutoUpdatePrompted.Value)
            {
                updatePrompt.Chosen = enable =>
                {
                    editorSettings.AutoUpdate.Value = enable;
                    editorSettings.AutoUpdatePrompted.Value = true;
                    maybeAutoPrepare();
                };
                updatePrompt.Show();
            }
        }

        /// <summary>Starts downloading the available update if the user has enabled automatic updates.</summary>
        private void maybeAutoPrepare()
        {
            if (updates == null)
                return;

            if (editorSettings.AutoUpdatePrompted.Value
                && editorSettings.AutoUpdate.Value
                && updates.State.Value == UpdateState.UpdateAvailable
                && updates.CanSelfInstall)
            {
                _ = updates.PrepareAsync();
            }
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // Resume the preview from wherever the editor was left (same set), not from the preview point.
            if (pushedEditor != null)
                currentPreview?.StartPreviewAt(pushedEditor.LastTime);
            else
                currentPreview?.StartPreview();

            // If the editor saved, osu! is importing the .osz in the background - refresh the library a
            // few times so the change shows up in our carousel once the import lands.
            if (pushedEditor?.DidSave == true)
            {
                Scheduler.AddDelayed(() => reloadBeatmaps(), 1500);
                Scheduler.AddDelayed(() => reloadBeatmaps(), 4000);
            }

            pushedEditor = null;
        }

        /// <summary>Reloads the beatmap library from osu!lazer's realm and repopulates the carousel.</summary>
        private void reloadBeatmaps(bool notify = false)
        {
            Task.Run(() =>
            {
                var loaded = BeatmapStore.LoadAll();
                Schedule(() =>
                {
                    sets = loaded;
                    carousel.SetBeatmaps(sets);
                    if (notify)
                        toasts?.Push($"Library reloaded - {sets.Count} sets", EditorTheme.Colours.Success);
                });
            });
        }

        /// <summary>Pauses/resumes the song preview, toasts which, and flips the play/pause button icon.</summary>
        private void togglePreview()
        {
            if (currentPreview == null)
                return;

            bool playing = currentPreview.TogglePause();
            toasts?.Push(playing ? "Preview playing" : "Preview paused",
                playing ? EditorTheme.Colours.Success : EditorTheme.Colours.TextMuted);
            updatePreviewToggleIcon(playing);
        }

        private void updatePreviewToggleIcon(bool playing) =>
            previewToggleButton.SetIcon(playing ? FontAwesome.Solid.Pause : FontAwesome.Solid.Play,
                playing ? "Pause preview (Ctrl+Space)" : "Play preview (Ctrl+Space)");

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            // Don't navigate the carousel while the new-beatmap dialog is open.
            if (newBeatmapOverlay.State.Value == Visibility.Visible)
                return base.OnKeyDown(e);

            // Ctrl/Cmd+Space pauses/resumes the song preview (with a toast confirming which).
            if (e.Key == Key.Space && Shortcut.CommandPressed(e))
            {
                togglePreview();
                return true;
            }

            switch (e.Key)
            {
                case Key.Down:
                    carousel.SelectNext();
                    return true;

                case Key.Up:
                    carousel.SelectPrevious();
                    return true;

                case Key.Right:
                    carousel.SelectNextSet();
                    return true;

                case Key.Left:
                    carousel.SelectPreviousSet();
                    return true;

                case Key.Enter:
                case Key.KeypadEnter:
                    openSelected();
                    return true;

                // F2 jumps to a random map; F5 reloads the library from osu!lazer's realm.
                case Key.F2:
                    carousel.SelectRandom();
                    return true;

                case Key.F5:
                    toasts?.Push("Reloading library...");
                    reloadBeatmaps(notify: true);
                    return true;
            }

            return base.OnKeyDown(e);
        }

        private void onSelectionChanged(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            selectedSet = set;
            selectedDiff = diff;

            infoPanel.SetMap(set, diff);

            // Reload the background whenever the *effective* background image changes - this covers both
            // moving to a new set and moving between difficulties that use a different background.
            string backgroundKey = $"{set.Identity}|{effectiveBackground(set, diff)}";
            if (loadedBackgroundKey != backgroundKey)
            {
                loadedBackgroundKey = backgroundKey;
                loadBackground(set, diff);
            }

            // Difficulties can use different audio files (e.g. "Boys Don't Cry"); only reload when the
            // effective audio actually changes, so same-audio diffs don't restart the preview.
            string audioKey = $"{set.Identity}|{effectiveAudio(set, diff)}";
            if (loadedAudioKey != audioKey)
            {
                loadedAudioKey = audioKey;
                loadPreview(set, diff);
            }
        }

        private static string effectiveBackground(BeatmapSetModel set, BeatmapDifficultyModel diff) =>
            diff.BackgroundFile.Length > 0 ? diff.BackgroundFile : set.BackgroundFile;

        /// <summary>This difficulty's audio filename, falling back to the first set difficulty that has one.</summary>
        private static string effectiveAudio(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            if (diff.AudioFile.Length > 0)
                return diff.AudioFile.ToLowerInvariant();

            foreach (var d in set.Difficulties)
                if (d.AudioFile.Length > 0)
                    return d.AudioFile.ToLowerInvariant();

            return string.Empty;
        }

        private void loadPreview(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            if (trackStore == null)
                return;

            int token = ++previewRequest;
            var preview = new PreviewTrack(set, diff, trackStore);

            LoadComponentAsync(preview, loaded =>
            {
                AddInternal(loaded);

                if (token != previewRequest || !this.IsCurrentScreen())
                {
                    loaded.Expire();
                    return;
                }

                currentPreview?.Stop();
                currentPreview?.Expire();
                currentPreview = loaded;
                loaded.StartPreview();
                updatePreviewToggleIcon(true);
            });
        }

        private void loadBackground(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            if (textures == null)
                return;

            int token = ++backgroundRequest;
            var background = new MapBackground(set, diff, textures) { RelativeSizeAxes = Axes.Both, Alpha = 0 };

            LoadComponentAsync(background, loaded =>
            {
                if (token != backgroundRequest)
                {
                    loaded.Expire();
                    return;
                }

                backgroundContainer.Add(loaded);
                loaded.FadeIn(400, Easing.OutQuint);

                var previous = currentBackground;
                currentBackground = loaded;
                previous?.FadeOut(400, Easing.OutQuint).Expire();
            });
        }

        private void openSelected()
        {
            if (selectedSet != null && selectedDiff != null)
                openEditor(selectedSet, selectedDiff);
        }

        private void openEditor(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            if (diff.OsuFileHash.Length == 0)
                return;

            pushedEditor = new EditorScreen(set, diff);
            this.Push(pushedEditor);
        }

        // --- Collab list helpers (downloaded-state, open, revision history) ---

        /// <summary>Resolves a collab to its local set+difficulty via the stored link key, against the loaded library.
        /// Returns false when the collab isn't linked here or its map has since been deleted.</summary>
        private bool tryResolveCollabMap(Online.CollabSummary c, out BeatmapSetModel? set, out BeatmapDifficultyModel? diff)
        {
            set = null;
            diff = null;

            string? key = collabs?.KeyForCollab(c.Id);
            if (key == null)
                return false;

            string[] parts = key.Split('|');
            if (parts.Length < 4)
                return false;

            foreach (var s in sets)
            {
                if (s.Artist != parts[0] || s.Title != parts[1] || s.Author != parts[2])
                    continue;

                foreach (var d in s.Difficulties)
                {
                    if (d.DifficultyName == parts[3])
                    {
                        set = s;
                        diff = d;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>A collab counts as downloaded only if its linked local map still exists (handles a deleted map).</summary>
        private bool collabIsDownloaded(Online.CollabSummary c) => tryResolveCollabMap(c, out _, out _);

        /// <summary>Opens the collab's local difficulty in the editor.</summary>
        private void openCollabMap(Online.CollabSummary c)
        {
            if (!tryResolveCollabMap(c, out var set, out var diff) || set == null || diff == null)
            {
                toasts?.Push("That map isn't downloaded anymore - download it again.");
                return;
            }

            collabsOverlay.Hide();
            openEditor(set, diff);
        }

        /// <summary>Opens the revision-history timeline for a collab.</summary>
        private void showCollabRevisions(Online.CollabSummary c)
        {
            string title = string.IsNullOrEmpty(c.Title) ? "(untitled)" : c.Title;
            revisionsOverlay.Show(title, () => auth?.Token is string tok
                ? Online.SobeApi.GetRevisionsAsync(tok, c.Id)
                : Task.FromResult(new List<Online.CollabRevisionSummary>()));
        }

        private void onBeatmapCreated(NewBeatmapRequest request)
        {
            // The set is already in lazer's realm (written directly), so reload and select + centre it.
            string identity = $"{request.Artist}|{request.Title}|{request.Creator}";
            carousel.SelectWhenLoaded(identity, request.DifficultyName, markNew: true);
            reloadBeatmaps();
        }

        private void onNewBeatmap() => newBeatmapOverlay.ShowForNewBeatmap();

        /// <summary>Signed in: shows the collab/friends actions. Logged out: shows the "sign in" card instead.</summary>
        private void updateAuthActions()
        {
            bool loggedIn = auth?.IsLoggedIn == true;
            authActions.FadeTo(loggedIn ? 1f : 0f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        // --- Top-bar action helpers ---

        /// <summary>A standalone 40px icon button sized for the top bar.</summary>
        private static MenuIconButton topBarButton(IconUsage icon, string tooltip, Action action) =>
            new MenuIconButton(icon, tooltip, action) { Size = new Vector2(40) };

        /// <summary>
        /// A small grouped "card" holding the playback controls — random map (F2), refresh library (F5),
        /// and the song-preview play/pause toggle — as three segments on a shared raised surface.
        /// </summary>
        private Drawable buildPlaybackCard()
        {
            const float button = 34;

            return new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Lg,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                        Padding = new MarginPadding(EditorTheme.Spacing.Xs),
                        Children = new Drawable[]
                        {
                            new MenuIconButton(FontAwesome.Solid.Random, "Random map (F2)",
                                () => carousel.SelectRandom()) { Size = new Vector2(button) },
                            new MenuIconButton(FontAwesome.Solid.Sync, "Refresh library (F5)",
                                () => { toasts?.Push("Reloading library..."); reloadBeatmaps(notify: true); }) { Size = new Vector2(button) },
                            previewToggleButton = new MenuIconButton(FontAwesome.Solid.Pause, "Pause preview (Ctrl+Space)",
                                togglePreview) { Size = new Vector2(button) },
                        },
                    },
                },
            };
        }

        // --- Context-menu actions (Create new Difficulty / Create new Set) ---

        private void onCreateDifficulty(BeatmapSetModel set, BeatmapDifficultyModel template)
        {
            if (template.OsuFileHash.Length == 0)
            {
                toasts?.Push("Can't add a difficulty to an unsaved map", EditorTheme.Colours.Warning);
                return;
            }

            pendingDifficultySet = set;
            pendingDifficultyTemplate = template;
            newDifficultyOverlay.Show(set.Difficulties.Select(d => d.DifficultyName));
        }

        private void onCreateDifficultyConfirmed(string name, bool copySettings, bool copyBpm, bool copySv)
        {
            var set = pendingDifficultySet;
            var template = pendingDifficultyTemplate;
            if (set == null || template == null)
                return;

            toasts?.Push($"Creating difficulty \"{name}\"...");

            Task.Run(() =>
            {
                // Write the new difficulty straight into lazer's realm (no importer, no launch).
                string? error = BeatmapRealmCreator.CreateDifficulty(set, template, name, copySettings, copyBpm, copySv);
                Schedule(() =>
                {
                    if (error == null)
                    {
                        toasts?.Push("Difficulty created", EditorTheme.Colours.Success);
                        // Select + centre the new difficulty once the reload brings it into the carousel.
                        carousel.SelectWhenLoaded(set.Identity, name);
                        reloadBeatmaps();
                    }
                    else
                    {
                        toasts?.Push($"Could not create difficulty: {error}", EditorTheme.Colours.Error);
                    }
                });
            });
        }

        private void onCreateSet(BeatmapSetModel set, BeatmapDifficultyModel donor)
        {
            string? osuPath = LazerFileStore.ResolvePath(set.DataDirectory, donor.OsuFileHash);
            if (osuPath == null)
            {
                toasts?.Push("Can't read the source map's audio/timing", EditorTheme.Colours.Warning);
                return;
            }

            string[] lines = File.ReadAllLines(osuPath);
            string audioName = BeatmapCloner.ExtractAudioFilename(lines);
            var timingLines = BeatmapCloner.ExtractTimingPointLines(lines);

            if (audioName.Length == 0 || !set.Files.TryGetValue(audioName.ToLowerInvariant(), out string? hash))
            {
                toasts?.Push("Source map has no resolvable audio", EditorTheme.Colours.Warning);
                return;
            }

            string? audioPath = LazerFileStore.ResolvePath(set.DataDirectory, hash);
            if (audioPath == null)
            {
                toasts?.Push("Source audio file is missing", EditorTheme.Colours.Warning);
                return;
            }

            newBeatmapOverlay.ShowSeeded(
                artist: set.Artist,
                title: set.Title,
                creator: defaultCreator(),
                audioStorePath: audioPath,
                audioFileName: audioName,
                timingLines: timingLines,
                bpm: firstBpm(timingLines));
        }

        /// <summary>The mapper name configured in editor settings (used as the creator for new sets).</summary>
        private string defaultCreator() => editorSettings.DefaultCreator.Value;

        /// <summary>BPM of the first uninherited timing point, or 120 if none can be parsed.</summary>
        private static double firstBpm(IReadOnlyList<string> timingLines)
        {
            foreach (string line in timingLines)
            {
                string[] parts = line.Split(',');
                // Field 6 is the uninherited flag (1 = red/BPM); legacy lines without it are uninherited.
                bool uninherited = parts.Length <= 6 || parts[6].Trim() == "1";
                if (uninherited && parts.Length >= 2
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength)
                    && beatLength > 0)
                    return 60000.0 / beatLength;
            }

            return 120;
        }

        // --- Delete (writes directly to osu!lazer's realm) ---

        private void onDeleteSet(BeatmapSetModel set)
        {
            confirmOverlay.Show(
                "Delete set",
                $"Delete \"{set.Artist} - {set.Title}\" from osu!lazer? This removes all its difficulties.",
                "Delete set",
                () => performDelete(() => BeatmapDeleter.DeleteSet(set), "Set deleted"));
        }

        private void onDeleteDifficulty(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            confirmOverlay.Show(
                "Delete difficulty",
                $"Delete the difficulty \"{diff.DifficultyName}\" from \"{set.Title}\"?",
                "Delete difficulty",
                () => performDelete(() => BeatmapDeleter.DeleteDifficulty(set, diff), "Difficulty deleted"));
        }

        /// <summary>Runs a realm delete off-thread, then toasts the result and refreshes the carousel.</summary>
        private void performDelete(Func<string?> delete, string successMessage)
        {
            Task.Run(() =>
            {
                string? error = delete();
                Schedule(() =>
                {
                    if (error == null)
                    {
                        toasts?.Push(successMessage, EditorTheme.Colours.Success);
                        reloadBeatmaps();
                    }
                    else
                    {
                        toasts?.Push(error, EditorTheme.Colours.Error);
                    }
                });
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            if (window != null && dragDropHandler != null)
                window.DragDrop -= dragDropHandler;

            textures?.Dispose();
            cardTextures?.Dispose();

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// Loads and displays a beatmap set's background image, decoding the .osu and the image
        /// off the update thread (it is added via LoadComponentAsync).
        /// </summary>
        private partial class MapBackground : CompositeDrawable
        {
            private readonly BeatmapSetModel set;
            private readonly BeatmapDifficultyModel diff;
            private readonly LargeTextureStore textures;

            public MapBackground(BeatmapSetModel set, BeatmapDifficultyModel diff, LargeTextureStore textures)
            {
                this.set = set;
                this.diff = diff;
                this.textures = textures;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Texture? texture = resolveTexture();
                if (texture == null)
                    return;

                InternalChild = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    FillMode = FillMode.Fill,
                    Texture = texture,
                };
            }

            private Texture? resolveTexture()
            {
                // Prefer the selected difficulty's own background, then fall back to any in the set.
                string preferred = diff.BackgroundFile.Length > 0 ? diff.BackgroundFile : set.BackgroundFile;
                if (preferred.Length > 0 && set.Files.TryGetValue(preferred.ToLowerInvariant(), out string? preferredHash) && preferredHash.Length >= 2)
                    return textures.Get($"{preferredHash[..1]}/{preferredHash[..2]}/{preferredHash}");

                foreach (var d in set.Difficulties)
                {
                    if (d.BackgroundFile.Length == 0 || !set.Files.TryGetValue(d.BackgroundFile.ToLowerInvariant(), out string? hash) || hash.Length < 2)
                        continue;

                    return textures.Get($"{hash[..1]}/{hash[..2]}/{hash}");
                }

                return null;
            }
        }

        /// <summary>
        /// Loads a set's audio track and its mapper-chosen preview point off the update thread,
        /// then plays from there. Disposed (and the track stopped) when expired.
        /// </summary>
        private partial class PreviewTrack : CompositeDrawable
        {
            // Gap between the track finishing and the preview looping back to the start, plus the fade-in length.
            private const double loop_gap_ms = 700;
            private const double loop_fade_ms = 450;

            private readonly BeatmapSetModel set;
            private readonly BeatmapDifficultyModel diff;
            private readonly ITrackStore trackStore;

            private Track? track;
            private int previewTime = -1;

            // While true the preview should keep playing (and loop when the track ends). Cleared on pause/stop.
            private bool looping;
            private bool restarting;
            private bool fading;
            private double fadeProgress = 1;

            public PreviewTrack(BeatmapSetModel set, BeatmapDifficultyModel diff, ITrackStore trackStore)
            {
                this.set = set;
                this.diff = diff;
                this.trackStore = trackStore;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                // Prefer this difficulty's own audio (from realm metadata); fall back to decoding the
                // .osu, then to any difficulty in the set that resolves to a stored audio file.
                if (tryLoad(diff))
                    return;

                foreach (var other in set.Difficulties)
                {
                    if (other != diff && tryLoad(other))
                        return;
                }
            }

            /// <summary>Attempts to resolve and load a difficulty's audio track; returns true on success.</summary>
            private bool tryLoad(BeatmapDifficultyModel candidate)
            {
                string audioFile = candidate.AudioFile;
                int preview = candidate.PreviewTime;

                // Metadata didn't carry the audio filename - decode the .osu as a fallback.
                if (audioFile.Length == 0 && candidate.OsuFileHash.Length > 0)
                {
                    string? osuPath = LazerFileStore.ResolvePath(set.DataDirectory, candidate.OsuFileHash);
                    if (osuPath != null)
                    {
                        var parsed = OsuFileDecoder.Decode(osuPath);
                        audioFile = parsed.AudioFilename;
                        if (preview < 0)
                            preview = parsed.PreviewTime;
                    }
                }

                if (audioFile.Length == 0 || !set.Files.TryGetValue(audioFile.ToLowerInvariant(), out string? hash) || hash.Length < 2)
                    return false;

                track = trackStore.Get($"{hash[..1]}/{hash[..2]}/{hash}");
                previewTime = preview;
                return track != null;
            }

            /// <summary>Seeks to the preview point (or 40% in if unset) and starts playback.</summary>
            public void StartPreview() => StartPreviewAt(previewTime >= 0 ? previewTime : (track?.Length ?? 0) * 0.4);

            /// <summary>Seeks to a specific time and starts playback (used to resume from the editor).</summary>
            public void StartPreviewAt(double time)
            {
                if (track == null)
                    return;

                // Don't clamp to track.Length here: the length may not be populated yet, and clamping
                // to 0 would make the preview play from the start instead of the preview point.
                track.Seek(Math.Max(0, time));
                track.Volume.Value = 1;
                track.Start();
                fading = false;
                fadeProgress = 1;
                looping = true;
            }

            /// <summary>Pauses the preview where it is, or resumes it if already paused (Ctrl+Space). Returns true if now playing.</summary>
            public bool TogglePause()
            {
                if (track == null)
                    return false;

                if (track.IsRunning)
                {
                    track.Stop();
                    looping = false;
                    return false;
                }

                track.Start();
                looping = true;
                return true;
            }

            public void Stop()
            {
                looping = false;
                track?.Stop();
            }

            protected override void Update()
            {
                base.Update();

                if (track == null)
                    return;

                // Ease the volume up after a loop restart (a soft fade-in rather than a hard cut).
                if (fading)
                {
                    fadeProgress = Math.Min(1, fadeProgress + Time.Elapsed / loop_fade_ms);
                    track.Volume.Value = fadeProgress;
                    if (fadeProgress >= 1)
                        fading = false;
                }

                // The track has finished on its own: after a short gap, loop back to the start with a fade-in.
                if (looping && !restarting && track.Length > 0 && !track.IsRunning && track.CurrentTime >= track.Length - 60)
                {
                    restarting = true;
                    Scheduler.AddDelayed(restartFromStart, loop_gap_ms);
                }
            }

            private void restartFromStart()
            {
                restarting = false;

                if (track == null || !looping)
                    return;

                track.Seek(0);
                track.Volume.Value = 0;
                fadeProgress = 0;
                fading = true;
                track.Start();
            }

            protected override void Dispose(bool isDisposing)
            {
                track?.Stop();
                track?.Dispose();
                base.Dispose(isDisposing);
            }
        }
    }
}
