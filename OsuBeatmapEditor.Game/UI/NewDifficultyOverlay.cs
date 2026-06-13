using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Small modal asking for a new difficulty's name (used by the carousel's "Create new Difficulty"
    /// action). Validates that the name is non-empty and not already used in the set.
    /// </summary>
    public partial class NewDifficultyOverlay : VisibilityContainer
    {
        /// <summary>Invoked with the confirmed difficulty name.</summary>
        public Action<string>? Confirmed;

        private Container panel = null!;
        private BasicTextBox nameBox = null!;
        private OsuButton createButton = null!;
        private SpriteText errorText = null!;

        private HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = osuTK.Graphics.Color4.Black,
                    Alpha = 0.6f,
                },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(460, 220),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = EditorTheme.Colours.Raised,
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, EditorTheme.Spacing.Md),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = "Create new Difficulty",
                                    Colour = EditorTheme.Colours.Accent,
                                    Font = EditorTheme.Type.Title(),
                                },
                                new SpriteText
                                {
                                    Text = "Difficulty name",
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Body(),
                                    Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
                                },
                                nameBox = new BasicTextBox
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = EditorTheme.Sizing.InputHeight,
                                    PlaceholderText = "e.g. Insane",
                                },
                                errorText = new SpriteText
                                {
                                    Colour = EditorTheme.Colours.Error,
                                    Font = EditorTheme.Type.Caption(),
                                    Alpha = 0,
                                },
                            },
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.BottomRight,
                            Origin = Anchor.BottomRight,
                            Margin = new MarginPadding(EditorTheme.Spacing.Xxl),
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                            Children = new Drawable[]
                            {
                                new OsuButton("Cancel", OsuColour.Surface)
                                {
                                    Size = new Vector2(120, 44),
                                    Action = () => Hide(),
                                },
                                createButton = new OsuButton("Create", OsuColour.Pink)
                                {
                                    Size = new Vector2(140, 44),
                                    Action = onConfirm,
                                },
                            },
                        },
                    },
                },
            };

            nameBox.Current.BindValueChanged(_ => validate());
            nameBox.OnCommit += (_, _) => onConfirm();
        }

        /// <summary>Opens the dialog, seeding a suggested unique name and remembering the set's existing names.</summary>
        public void Show(IEnumerable<string> existing)
        {
            existingNames = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            nameBox.Text = suggestName();
            errorText.Alpha = 0;
            Show();
            Schedule(() => GetContainingFocusManager()?.ChangeFocus(nameBox));
        }

        private string suggestName()
        {
            const string baseName = "New Difficulty";
            if (!existingNames.Contains(baseName))
                return baseName;

            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName} {i}";
                if (!existingNames.Contains(candidate))
                    return candidate;
            }
        }

        private string trimmedName => nameBox.Text.Trim();

        private bool isValid =>
            trimmedName.Length > 0 && !existingNames.Contains(trimmedName);

        private void validate()
        {
            createButton.Enabled.Value = isValid;
            errorText.Alpha = trimmedName.Length > 0 && existingNames.Contains(trimmedName) ? 1 : 0;
            errorText.Text = "That difficulty name already exists in this set.";
        }

        private void onConfirm()
        {
            if (!isValid)
                return;

            Confirmed?.Invoke(trimmedName);
            Hide();
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            validate();
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.96f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Hide();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>Swallows clicks inside the panel so they don't dismiss the overlay.</summary>
        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
