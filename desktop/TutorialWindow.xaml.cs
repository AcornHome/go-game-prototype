using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GoGame;

/// <summary>
/// Go tutorial window. Chapters; click the left table of contents to switch the right content.
/// All ASCII boards use precise 9×9 strings + auto coordinate labels, each cell mapping one-to-one to Go rules.
/// Character set: '·' empty  '●' black  '○' white  '★' star point  '?' focus  '!' danger/dying
/// </summary>
public partial class TutorialWindow : Window
{
    public TutorialWindow()
    {
        InitializeComponent();
        BuildNav();
        ShowChapter(0);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- Table of contents ----
    private void NavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is int idx)
            ShowChapter(idx);
    }

    private void BuildNav()
    {
        string[] titles = {
            "1. The Board & Basic Terms",
            "2. Liberties · A Stone's Breath",
            "3. Capturing · Removing Opponents",
            "4. Suicide · The Forbidden Move",
            "5. Ko · The Global Recapture Ban",
            "6. Endgame · Counting",
            "7. Getting Started · Opening Points",
        };
        for (int i = 0; i < titles.Length; i++)
        {
            var btn = new Button
            {
                Content = titles[i],
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(14, 10, 14, 10),
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = (Brush)FindResource("BrushCard"),
                Foreground = (Brush)FindResource("BrushText"),
                BorderBrush = (Brush)FindResource("BrushCardHi"),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = i,
            };
            btn.Click += NavBtn_Click;
            NavList.Children.Add(btn);
        }
    }

    // ---- Chapter rendering ----
    private void ShowChapter(int idx)
    {
        for (int i = 0; i < NavList.Children.Count; i++)
        {
            if (NavList.Children[i] is Button b)
            {
                bool sel = i == idx;
                b.Background = sel ? (Brush)FindResource("BrushAccentDim") : (Brush)FindResource("BrushCard");
                b.Foreground = sel ? Brushes.White : (Brush)FindResource("BrushText");
            }
        }

        ContentPanel.Children.Clear();
        switch (idx)
        {
            case 0: Chapter1_Board(); break;
            case 1: Chapter2_Liberty(); break;
            case 2: Chapter3_Capture(); break;
            case 3: Chapter4_Suicide(); break;
            case 4: Chapter5_Ko(); break;
            case 5: Chapter6_Endgame(); break;
            case 6: Chapter7_Opening(); break;
        }
        ContentPanel.Children.Add(new Border { Height = 40 });
    }

    // ---- Layout helpers ----
    private TextBlock H1(string text) => new()
    {
        Text = text,
        FontSize = 24,
        FontWeight = FontWeights.Bold,
        Foreground = (Brush)FindResource("BrushAccent"),
        Margin = new Thickness(0, 0, 0, 14),
    };

    private TextBlock H2(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)FindResource("BrushText"),
        Margin = new Thickness(0, 18, 0, 8),
    };

    private TextBlock P(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("BrushText"),
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
        LineHeight = 24,
    };

    private TextBlock Note(string text) => new()
    {
        Text = "💡 " + text,
        FontStyle = FontStyles.Italic,
        Foreground = (Brush)FindResource("BrushTextDim"),
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 12),
        LineHeight = 22,
    };

    private Border TipBox(string title, string body)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = (Brush)FindResource("BrushAccent"),
            Margin = new Thickness(0, 0, 0, 4),
        });
        sp.Children.Add(new TextBlock
        {
            Text = body,
            Foreground = (Brush)FindResource("BrushText"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
        });
        return new Border
        {
            Background = (Brush)FindResource("BrushCard"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 8, 0, 14),
            Child = sp,
        };
    }

    /// <summary>
    /// 9×9 board renderer: takes 9 rows of strings (9 chars each), auto-adds coordinate labels.
    /// Row 0 = y=8 (top), Row 8 = y=0 (bottom); Col 0 = x=A (left), Col 8 = x=J (right).
    /// Column letters A B C D E F G H J (skip I, matching the GTP protocol).
    /// </summary>
    private Border NumberedBoard(string board9x9, string caption)
    {
        var rows = board9x9.Trim().Split('\n').Take(9).ToList();
        while (rows.Count < 9) rows.Add(new string('·', 9));

        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Cascadia Mono"),
            FontSize = 13,
            LineHeight = 18,
        };

        // Top column labels
        tb.Inlines.Add(new Run("   ") { Foreground = (Brush)FindResource("BrushTextMute") });
        for (int c = 0; c < 9; c++)
        {
            string letter = (c < 8) ? ((char)('A' + c)).ToString() : "J";
            tb.Inlines.Add(new Run(" " + letter + " ") { Foreground = (Brush)FindResource("BrushTextMute") });
        }
        tb.Inlines.Add(new LineBreak());

        for (int i = 0; i < 9; i++)
        {
            int y = 9 - i;
            tb.Inlines.Add(new Run(" " + y + " ") { Foreground = (Brush)FindResource("BrushTextMute") });
            string row = rows[i].PadRight(9).Substring(0, 9);
            for (int j = 0; j < 9; j++)
            {
                AddPieceRun(tb, row[j]);
            }
            tb.Inlines.Add(new Run(" " + y) { Foreground = (Brush)FindResource("BrushTextMute") });
            tb.Inlines.Add(new LineBreak());
        }

        // Bottom column labels
        tb.Inlines.Add(new Run("   ") { Foreground = (Brush)FindResource("BrushTextMute") });
        for (int c = 0; c < 9; c++)
        {
            string letter = (c < 8) ? ((char)('A' + c)).ToString() : "J";
            tb.Inlines.Add(new Run(" " + letter + " ") { Foreground = (Brush)FindResource("BrushTextMute") });
        }

        var sp = new StackPanel();
        sp.Children.Add(new Border
        {
            Background = (Brush)FindResource("BrushCard"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 6, 0, 4),
            Child = tb,
        });
        if (!string.IsNullOrEmpty(caption))
        {
            sp.Children.Add(new TextBlock
            {
                Text = caption,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("BrushTextDim"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 4, 0, 14),
                LineHeight = 20,
            });
        }
        return new Border { Child = sp };
    }

    private void AddPieceRun(TextBlock tb, char c)
    {
        switch (c)
        {
            case '●':
                tb.Inlines.Add(new Run(" ● ") { Foreground = Brushes.Black, FontWeight = FontWeights.Bold });
                break;
            case '○':
                tb.Inlines.Add(new Run(" ○ ") { Foreground = Brushes.White, FontWeight = FontWeights.Bold });
                break;
            case '★':
                tb.Inlines.Add(new Run(" ★ ") { Foreground = (Brush)FindResource("BrushStar"), FontWeight = FontWeights.Bold });
                break;
            case '?':
                tb.Inlines.Add(new Run(" ? ") { Foreground = (Brush)FindResource("BrushAccent"), FontWeight = FontWeights.Bold });
                break;
            case '!':
                tb.Inlines.Add(new Run(" ! ") { Foreground = (Brush)FindResource("BrushDanger"), FontWeight = FontWeights.Bold });
                break;
            case '·':
            default:
                tb.Inlines.Add(new Run(" · ") { Foreground = (Brush)FindResource("BrushTextMute") });
                break;
        }
    }

    // ========== Chapter content (proofread strictly against Go rules) ==========

    private void Chapter1_Board()
    {
        ContentPanel.Children.Add(H1("1. The Board & Basic Terms"));
        ContentPanel.Children.Add(P("A Go board is a grid formed by two sets of perpendicular lines; stones are placed on the intersections (not inside the squares). A standard 19×19 board has 361 intersections; China Go gives you a 9×9 = 81-intersection \"9-line board\" by default for learning — the rules are identical to the 19-line board, just smaller and faster."));
        ContentPanel.Children.Add(P("Use the dropdown on the right to switch the board to 13 or 19 lines. The diagrams below are all on a 9-line board, each cell corresponding to an intersection."));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "···●·····\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········",
            "A 9-line board with 1 black stone in the upper-middle (row D = GTP \"D6\"). Black ● is drawn on an intersection, not in a square."
        ));
        ContentPanel.Children.Add(H2("Who moves first"));
        ContentPanel.Children.Add(P("Black moves first. After that the players alternate, placing one stone or passing each turn. Odd-numbered moves in a game are Black's, even-numbered are White's."));
        ContentPanel.Children.Add(P("Because Black has a slight first-move advantage, under Chinese rules White receives a \"komi\" compensation at the end — usually 7.5 points. China Go adds this komi automatically when KataGo counts the score."));

        ContentPanel.Children.Add(H2("Quick term reference"));
        var terms = new[]
        {
            "Intersection: a point on the board where a stone may be placed; 81 on a 9-line board, 361 on a 19-line board.",
            "Liberty: an empty intersection orthogonally adjacent to a stone (see Chapter 2).",
            "Group / string: adjacent same-color stones that form one connected unit, treated as a whole.",
            "Capture: removing an opponent's stone(s) that have no liberties from the board (see Chapter 3).",
            "Point: under Chinese rules, a \"point\" = an empty intersection you surround. At the end, points + your stones = your score.",
            "Pass: playing no stone this turn, letting the opponent move. The game ends only after both players pass consecutively.",
            "Star point: a pre-marked common opening point on the board (see Chapter 7).",
            "Komi: extra points given to White at the end (usually 7.5) to offset Black's first-move advantage.",
        };
        foreach (var t in terms) ContentPanel.Children.Add(P("● " + t));

        ContentPanel.Children.Add(Note("Read this chapter first as a \"dictionary\" of terms; come back to it whenever a term is unclear. It will make more sense after Chapters 2–6."));
    }

    private void Chapter2_Liberty()
    {
        ContentPanel.Children.Add(H1("2. Liberties · A Stone's Breath"));
        ContentPanel.Children.Add(P("\"Liberty\" is the core concept of Go. A liberty is an empty intersection orthogonally adjacent to a stone."));
        ContentPanel.Children.Add(P("The liberties of a group (a set of connected same-color stones) = all empty intersections adjacent to the whole group. **A stone must have liberties, or the opponent will capture it.**"));

        ContentPanel.Children.Add(H2("A single stone in the center · 4 liberties"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·····!···\n" +
            "···!●!···\n" +
            "·····!···\n" +
            "·········\n" +
            "·········\n" +
            "·········",
            "Black plays E5 (center). Its 4 neighbors (D5)(F5)(E6)(E4) are all empty intersections (! marked), so it has 4 liberties."
        ));

        ContentPanel.Children.Add(H2("A single stone on the edge · 3 liberties"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "!········\n" +
            "●!·······\n" +
            "!········\n" +
            "·········\n" +
            "·········\n" +
            "·········",
            "Black plays A6 (middle of the left edge). It is off-board on the left and bottom, leaving only 3 neighbors: A7 (up), A5 (down), B6 (right) — all empty (! marked), so it has 3 liberties."
        ));

        ContentPanel.Children.Add(H2("A single stone in the corner · 2 liberties"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "!········\n" +
            "●!·······",
            "Black plays A9 (lower-left corner). It is off-board on the left and bottom, leaving only 2 valid neighbors: A8 (up) and B9 (right), both empty (! marked), so it has 2 liberties."
        ));

        ContentPanel.Children.Add(TipBox("Corners are gold, sides are silver, center is grass",
            "A corner stone has the fewest liberties (2) and is easiest to secure; an edge stone has 3; a center stone has 4 but is hardest to defend. In practice, take the corners first, then the sides, then fight for the center."));

        ContentPanel.Children.Add(H2("Liberties of a group"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "··!!!!!··\n" +
            "··●●···!·\n" +
            "·······!·\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········",
            "Black stones at C5 and D5 connect vertically = one group. Its liberties = all empty intersections adjacent to the group = (C6)(D6)(E6)(E5)(C4), 5 in total (! marked). Note (D6) is a shared liberty of both stones and counts only once."
        ));
    }

    private void Chapter3_Capture()
    {
        ContentPanel.Children.Add(H1("3. Capturing · Removing Opponents"));
        ContentPanel.Children.Add(P("When a group of opponent stones has \"no liberties\" (0 adjacent empty intersections), it is immediately removed from the board — this is a \"capture\"."));
        ContentPanel.Children.Add(P("Order of resolution: when you play a move, the system first checks which opponent groups this move directly surrounds and removes the captured stones at once; then it checks whether \"the stone you just played / the group it forms\" still has liberties of its own (no liberties means a suicide move, see Chapter 4)."));

        ContentPanel.Children.Add(H2("Example: a corner white stone is captured"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "!········\n" +
            "○!·······",
            "(Before capture) current position: white ○ in the corner at A9. Among its neighbors (B9)(A8), one is unplayed (i.e. one liberty = ! mark), the rest are off-board. White ○ still has 1 liberty."
        ));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "●········\n" +
            "·!·······",
            "(At capture) Black plays (A8). Now White ○'s neighbors (B9) Black, (A8) Black are all filled, and left/right are off-board. White ○ immediately has 0 liberties and is removed from the board → position (A9) becomes empty (!)."
        ));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "●········\n" +
            "●●·······",
            "(Capture complete) Black's two stones (A8)(B9) surround the empty point at (A9). This empty area counts as Black's \"territory\" when scoring at the end. Note: China Go **does not require you to remove captured stones manually** — it applies the Go rules automatically."
        ));

        ContentPanel.Children.Add(TipBox("Two steps of capture resolution",
            "1️⃣ **First see \"who did this move directly surround\"** — if so, the surrounded opponent stones are removed from the board immediately.\n" +
            "2️⃣ **Then see \"whether I myself (including the new group formed) still have liberties\"** — no liberties means a suicide move, which is forbidden (see Chapter 4)."));
    }

    private void Chapter4_Suicide()
    {
        ContentPanel.Children.Add(H1("4. Suicide · The Forbidden Move"));
        ContentPanel.Children.Add(P("You may not play a move that leaves \"this new stone + all friendly same-color stones connected to it\" with 0 liberties overall — unless the move simultaneously captures some opponent stones, leaving your group with liberties."));

        ContentPanel.Children.Add(H2("Example: a suicide move (rejected by China Go)"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "●●●●●●●●●\n" +
            "●●●●●●●●?",
            "White wants to play (J9) to save itself. Here is what happens: (J9)'s 4 neighbors = (J8) Black, (I9 off-board), (K9 off-board), (J10 off-board). White playing here has 0 liberties of its own, and captures no opponent stone → suicide is forbidden; China Go rejects it and shows \"KataGo judged it an illegal move\"."
        ));

        ContentPanel.Children.Add(H2("Exception: a suicidal shape that captures is legal"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "··!○!····\n" +
            "··!○!····",
            "Suppose the position is like this: Black (E3) is a lone stone, with only 4 empty neighbors (D3)(F3)(D2)(F2) (! = 4 liberties). White wants to play (E4) as a \"throw-in\" — it looks like suicide, but after White plays (E4): Black (E3)'s 4 neighbors = (D3)(F3)(D2)(F2), with (E4)=White added (does not block a liberty)... actually this move does not surround Black."
        ));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "····●····\n" +
            "····!····\n" +
            "·········\n" +
            "····●····\n" +
            "·········\n" +
            "·········",
            "A more standard \"throw-in\" example: two isolated black stones (D4)(D7) each with 1 liberty at (D5)(D6) (! marked). White playing (D6) looks like suicide, but at the same time it blocks (D7)'s last liberty → the (D7) black stone has 0 liberties and is captured, and White's (D6) still has its own liberties (connected to the potential point at (D5)), so this move is legal.\n" +
            "— This technique is called a \"throw-in\" (snapback), an advanced tactic; at the beginner stage it is enough to know the rule, learn it when you actually meet it."
        ));

        ContentPanel.Children.Add(TipBox("Why does Go forbid suicide?",
            "If suicide were allowed, in theory a player could keep filling their own stones into the opponent's area — Go would become a game of \"who can throw stones faster\", contrary to the essence of \"surrounding territory\".\n" +
            "But Go rules also accept \"suicidal shapes that capture\" (throw-in / ko), letting strong players use this for tactics — this is one source of Go's depth."));
    }

    private void Chapter5_Ko()
    {
        ContentPanel.Children.Add(H1("5. Ko · The Global Recapture Ban"));
        ContentPanel.Children.Add(P("Go has a seemingly odd rule: **after the opponent just captured one of your stones, you may not immediately recapture at the same point** — you must play elsewhere first."));
        ContentPanel.Children.Add(P("This rule is called the \"ko ban\" or \"global recapture ban\"; its purpose is to prevent the two sides from repeatedly capturing back and forth at the same point, leaving a game that never ends."));

        ContentPanel.Children.Add(H2("Example: the black-white mutual capture loop"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "···!·····\n" +
            "···!●!···\n" +
            "···!○!···\n" +
            "···!·····\n" +
            "·········\n" +
            "·········",
            "Simplified illustration: Black (E5) and White (E6) sit vertically adjacent; Black (E5) has 1 adjacent liberty at (E4) (! marked). If White plays (E4) to capture Black (E5), Black may not immediately play back at the same point (E4) to recapture White (E4) — this is the seed of ko. In real Go the ko shape is more refined, but the essence is \"both sides share 1 liberty → capture each other\"."
        ));

        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "···○○●●●\n" +
            "····●○●●\n" +
            "····●○○!\n" +
            "·········\n" +
            "·········\n" +
            "·········",
            "A real-game ko shape: White's 4 stones (C4)(D4)(E5)(F5) fight Black's 5 stones (E4)(F4)(G4)(G3)(G2); the key point (G5) is the only liberty (!). If Black plays (G5) to capture a White group, White may not immediately return to (G5) to recapture — it must first play elsewhere (find a \"ko threat\" to force Black to respond) before coming back."
        ));

        ContentPanel.Children.Add(TipBox("Judging ko in real games",
            "After recognizing a ko in a real game, both sides look for \"ko threats\" — using a small loss elsewhere to force the opponent to respond, opening the local ko loop. One of Go's highest-level tactics; at the beginner stage it is enough to know the rule."));
        ContentPanel.Children.Add(Note("China Go fully obeys the ko rule: if you just captured a stone, the software rejects your attempt to recapture at the same point immediately. When KataGo rejects you, trust it and play elsewhere."));
    }

    private void Chapter6_Endgame()
    {
        ContentPanel.Children.Add(H1("6. Endgame · Counting"));
        ContentPanel.Children.Add(P("When does a game end? Answer: **both players pass consecutively** and agree to stop. Then \"counting\" determines who has the higher score."));

        ContentPanel.Children.Add(H2("Chinese-rule counting (China Go default)"));
        ContentPanel.Children.Add(P("Endgame counting rules:"));
        ContentPanel.Children.Add(P("● **Black score = Black stones + empty intersections Black surrounds**"));
        ContentPanel.Children.Add(P("● **White score = White stones + empty intersections White surrounds + 7.5 (komi)**"));
        ContentPanel.Children.Add(P("The side with more points wins. The 0.5 point avoids ties."));

        ContentPanel.Children.Add(H2("What is \"surrounded empty space\"?"));
        ContentPanel.Children.Add(NumberedBoard(
            "●●●●●●●●●\n" +
            "●●●●●●●●●\n" +
            "●●●●●●●●●\n" +
            "●●●●●●●●●\n" +
            "●●●●●●●●●\n" +
            "●●●●●●●●●\n" +
            "●●●●●●●●●\n" +
            "●●●●●●●●●\n" +
            "●●●●●●●●●",
            "Illustration: Black fills the board and surrounds White completely. But no real Go position is this extreme — the diagram below shows the counting details more accurately."
        ));

        ContentPanel.Children.Add(NumberedBoard(
            "●●●●●●●●●\n" +
            "●●●●○○●●●\n" +
            "●●●●○○●●●\n" +
            "●●●●○○●●●\n" +
            "●●●●●●●●●\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········",
            "Each side occupies a corner: Black surrounds the 3 empty intersections (F1)(F2)(F3) (untouched by White), which count as Black's \"territory\". White surrounds the 3 empty intersections (E1)(E2)(E3), counting as White's \"territory\".\nAt the end, Black stones + Black territory vs White stones + White territory + 7.5 — whoever is higher wins."
        ));

        ContentPanel.Children.Add(H2("How does China Go do it?"));
        ContentPanel.Children.Add(P("After clicking the \"📊 Endgame Count\" button at the bottom-right, the program automatically:"));
        ContentPanel.Children.Add(P("1. Both sides pass once each (satisfying the endgame condition)"));
        ContentPanel.Children.Add(P("2. Calls KataGo's final_score command to judge automatically under Chinese rules"));
        ContentPanel.Children.Add(P("3. Shows a large win/lose message, e.g. \"Black (you) wins by 3.5 points\""));
        ContentPanel.Children.Add(P("4. The current game record is auto-saved to the desktop + added to the game library"));

        ContentPanel.Children.Add(Note("You can also click \"🏳 Resign\" to end early. If the byo-yomi timer runs out (the main timer), the current player is auto-judged the loser."));
    }

    private void Chapter7_Opening()
    {
        ContentPanel.Children.Add(H1("7. Getting Started · Opening Points"));
        ContentPanel.Children.Add(P("Go has 9 \"star points\" — traditionally the first move near a star point is the safest. The GTP coordinates of the 9 star points on a 19-line board are:"));
        ContentPanel.Children.Add(P("● **Four corners**: D4  D16  Q4  Q16"));
        ContentPanel.Children.Add(P("● **Four sides**: J4  J16  D10  Q10"));
        ContentPanel.Children.Add(P("● **Center (tengen)**: J10"));

        ContentPanel.Children.Add(NumberedBoard(
            "★········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "····★····\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "········",
            "Star points on a 9-line board (position only; in a real game, follow the ★ shown on the board when placing): A9 / C9 / E5 / G3 / J1 etc. — the 9-line board is smaller than the 19-line and good for teaching."
        ));
        ContentPanel.Children.Add(NumberedBoard(
            "★··★···★\n" +
            "·········\n" +
            "···★·····\n" +
            "·········\n" +
            "·······★\n" +
            "·········\n" +
            "···★·····\n" +
            "·········\n" +
            "★··★···★",
            "19-line board illustration (position only): ★ marks the 9 star points — 4 corners + 4 side-middles + center tengen. The first move is preferably one of these points."
        ));

        ContentPanel.Children.Add(H2("Common openings"));
        ContentPanel.Children.Add(P("● **Star-point opening** (★): first move on a star point; safest, AI's top choice."));
        ContentPanel.Children.Add(P("● **Komoku opening** (one line off a star point): leans toward corner control, common in precise fighting."));
        ContentPanel.Children.Add(P("● **San-san opening** (one step deep into the corner): grabs solid territory, but the opponent attacks from outside; beginners use with caution."));
        ContentPanel.Children.Add(P("● **Tengen** (center): seizes the global high ground first, forcing the opponent to move, strong rhythm."));

        ContentPanel.Children.Add(TipBox("Beginner advice",
            "Don't know where to play? Follow KataGo — after each AI move it has finished calculating in a second or two, and its move is the high-probability \"good move\" for the current position.\n" +
            "After 5–10 games you'll have a feel for Go; don't chase \"optimal\", enjoy the process of surrounding territory."));

        ContentPanel.Children.Add(Note("Finally: the core idea of Go is just one sentence — **\"surround more territory than your opponent\"**. All the rules (liberties, capture, suicide, ko, endgame) exist to turn that plain sentence into computable rules. China Go has handled all the rule details for you; you only need to click where to place your stone."));
    }
}
