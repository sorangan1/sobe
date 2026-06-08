using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
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
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Screens.Edit;
using OsuBeatmapEditor.Game.UI;
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
        private Container backgroundContainer = null!;
        private OsuButton newBeatmapButton = null!;
        private NewBeatmapOverlay newBeatmapOverlay = null!;

        private IReadOnlyList<BeatmapSetModel> sets = Array.Empty<BeatmapSetModel>();

        private LargeTextureStore? textures;
        private Drawable? currentBackground;
        private int backgroundRequest;

        private ITrackStore? trackStore;
        private PreviewTrack? currentPreview;
        private int previewRequest;

        private BeatmapSetModel? selectedSet;
        private BeatmapDifficultyModel? selectedDiff;
        private string? loadedSetIdentity;
        private string? loadedBackgroundKey;
        private EditorScreen? pushedEditor;

        [BackgroundDependencyLoader]
        private void load(GameHost host, AudioManager audio)
        {
            sets = BeatmapStore.LoadAll();

            string? dataDir = LazerStorage.FindDataDirectory();
            if (dataDir != null)
            {
                var fileStore = new StorageBackedResourceStore(new NativeStorage(Path.Combine(dataDir, "files"), host));
                textures = new LargeTextureStore(host.Renderer, host.CreateTextureLoaderStore(fileStore), TextureFilteringMode.Linear, false);
                trackStore = audio.GetTrackStore(new StorageBackedResourceStore(new NativeStorage(Path.Combine(dataDir, "files"), host)));
            }

            BasicTextBox searchBox;
            BasicDropdown<SortMode> sortDropdown;

            // A context-menu container wraps everything so right-clicking a difficulty can offer "Edit".
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
                newBeatmapButton = new OsuButton("New Beatmap", OsuColour.Pink)
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Size = new Vector2(220, 56),
                    Margin = new MarginPadding(30),
                    Action = onNewBeatmap,
                },
                rightArea = new Container
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Y,
                    Width = carousel_width,
                    Padding = new MarginPadding { Top = 24, Bottom = 24, Right = 24 },
                    Children = new Drawable[]
                    {
                        // Carousel first, toolbar second so the sort dropdown's popup draws above the list.
                        carousel = new BeatmapCarousel
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = 52 },
                            SelectionChanged = onSelectionChanged,
                        },
                        new GridContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 40,
                            ColumnDimensions = new[]
                            {
                                new Dimension(),
                                new Dimension(GridSizeMode.Absolute, 12),
                                new Dimension(GridSizeMode.Absolute, 150),
                            },
                            Content = new[]
                            {
                                new Drawable?[]
                                {
                                    searchBox = new BasicTextBox
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 40,
                                        PlaceholderText = "Search...",
                                    },
                                    null,
                                    sortDropdown = new BasicDropdown<SortMode>
                                    {
                                        RelativeSizeAxes = Axes.X,
                                    },
                                },
                            },
                        },
                    },
                },
                newBeatmapOverlay = new NewBeatmapOverlay
                {
                    Created = onBeatmapCreated,
                },
                    },
                },
            };

            carousel.EditRequested = openEditor;
            carousel.Textures = textures;

            sortDropdown.Items = Enum.GetValues<SortMode>();
            sortDropdown.Current.Value = SortMode.Artist;
            sortDropdown.Current.BindValueChanged(e => carousel.SetSort(e.NewValue));

            searchBox.Current.BindValueChanged(e => carousel.SetFilter(e.NewValue));

            carousel.SetBeatmaps(sets);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            rightArea.MoveToX(40).FadeOut();
            rightArea.MoveToX(0, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);

            newBeatmapButton.MoveToY(40).FadeOut();
            newBeatmapButton.Delay(150).MoveToY(0, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);
            // Don't let the preview keep playing over the editor's own audio.
            currentPreview?.Stop();
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
                Scheduler.AddDelayed(reloadBeatmaps, 1500);
                Scheduler.AddDelayed(reloadBeatmaps, 4000);
            }

            pushedEditor = null;
        }

        /// <summary>Reloads the beatmap library from osu!lazer's realm and repopulates the carousel.</summary>
        private void reloadBeatmaps()
        {
            Task.Run(() =>
            {
                var loaded = BeatmapStore.LoadAll();
                Schedule(() =>
                {
                    sets = loaded;
                    carousel.SetBeatmaps(sets);
                });
            });
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            // Don't navigate the carousel while the new-beatmap dialog is open.
            if (newBeatmapOverlay.State.Value == Visibility.Visible)
                return base.OnKeyDown(e);

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

                case Key.F5:
                    reloadBeatmaps();
                    return true;
            }

            return base.OnKeyDown(e);
        }

        private void onSelectionChanged(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            selectedSet = set;
            selectedDiff = diff;

            // Reload the background whenever the *effective* background image changes - this covers both
            // moving to a new set and moving between difficulties that use a different background.
            string backgroundKey = $"{set.Identity}|{effectiveBackground(set, diff)}";
            if (loadedBackgroundKey != backgroundKey)
            {
                loadedBackgroundKey = backgroundKey;
                loadBackground(set, diff);
            }

            // Audio is shared across a set's difficulties; only reload on a set change.
            if (loadedSetIdentity != set.Identity)
            {
                loadedSetIdentity = set.Identity;
                loadPreview(set);
            }
        }

        private static string effectiveBackground(BeatmapSetModel set, BeatmapDifficultyModel diff) =>
            diff.BackgroundFile.Length > 0 ? diff.BackgroundFile : set.BackgroundFile;

        private void loadPreview(BeatmapSetModel set)
        {
            if (trackStore == null)
                return;

            int token = ++previewRequest;
            var preview = new PreviewTrack(set, trackStore);

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

        private void onBeatmapCreated(NewBeatmapRequest request)
        {
            var model = new BeatmapSetModel
            {
                OnlineID = -1,
                Artist = request.Artist,
                Title = request.Title,
                Author = request.Creator,
                DataDirectory = LazerStorage.FindDataDirectory() ?? string.Empty,
                Difficulties = new List<BeatmapDifficultyModel>
                {
                    new BeatmapDifficultyModel
                    {
                        DifficultyName = request.DifficultyName,
                        RulesetShortName = "osu",
                    },
                },
                SearchText = $"{request.Artist} {request.Title} {request.Creator}".ToLowerInvariant(),
            };

            carousel.AddNewBeatmap(model);
        }

        private void onNewBeatmap() => newBeatmapOverlay.Show();

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
            private readonly BeatmapSetModel set;
            private readonly ITrackStore trackStore;

            private Track? track;
            private int previewTime = -1;

            public PreviewTrack(BeatmapSetModel set, ITrackStore trackStore)
            {
                this.set = set;
                this.trackStore = trackStore;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                foreach (var diff in set.Difficulties)
                {
                    if (diff.OsuFileHash.Length == 0)
                        continue;

                    string? osuPath = LazerFileStore.ResolvePath(set.DataDirectory, diff.OsuFileHash);
                    if (osuPath == null)
                        continue;

                    var parsed = OsuFileDecoder.Decode(osuPath);
                    if (parsed.AudioFilename.Length == 0 || !set.Files.TryGetValue(parsed.AudioFilename.ToLowerInvariant(), out string? hash))
                        continue;

                    track = trackStore.Get($"{hash[..1]}/{hash[..2]}/{hash}");
                    previewTime = parsed.PreviewTime;
                    break;
                }
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
                track.Start();
            }

            public void Stop() => track?.Stop();

            protected override void Dispose(bool isDisposing)
            {
                track?.Stop();
                track?.Dispose();
                base.Dispose(isDisposing);
            }
        }
    }
}
