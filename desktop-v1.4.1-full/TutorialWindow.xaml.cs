using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GoGame;

/// <summary>
/// 围棋入门教程窗口。分章节，点击左侧目录切换右侧内容。
/// 全部 ASCII 棋盘采用精确 9×9 字符串 + 自动坐标号，每一格一一对应围棋规则。
/// 字符集：'·' 空  '●' 黑  '○' 白  '★' 星位  '?' 焦点  '!' 危险/濒死
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

    // ---- 目录 ----
    private void NavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is int idx)
            ShowChapter(idx);
    }

    private void BuildNav()
    {
        string[] titles = {
            "1. 棋盘与基本术语",
            "2. 气 · 棋子的呼吸",
            "3. 提子 · 吃掉对方",
            "4. 自杀 · 禁止规则",
            "5. 劫 · 全局禁提",
            "6. 终局判定 · 数子",
            "7. 实战入门 · 开局点",
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

    // ---- 章节渲染 ----
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

    // ---- 排版辅助 ----
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
    /// 9×9 棋盘渲染器：传入 9 行字符串（每行 9 个字符），自动加坐标号。
    /// 行 0 = y=8（顶部），行 8 = y=0（底部）；列 0 = x=A（左），列 8 = x=J（右）。
    /// 列号用 A B C D E F G H J（跳过 I，与 GTP 协议一致）。
    /// </summary>
    private Border NumberedBoard(string board9x9, string caption)
    {
        var rows = board9x9.Trim().Split('\n').Take(9).ToList();
        while (rows.Count < 9) rows.Add(new string('·', 9));

        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei UI"),
            FontSize = 13,
            LineHeight = 18,
        };

        // 顶部列号
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

        // 底部列号
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

    // ========== 章节内容（已严格按围棋规则校对） ==========

    private void Chapter1_Board()
    {
        ContentPanel.Children.Add(H1("1. 棋盘与基本术语"));
        ContentPanel.Children.Add(P("围棋棋盘是两组垂直线交叉而成的网格，棋子落在「交叉点」（不是格子里）。标准 19×19 棋盘 = 361 个交叉点；中国围棋 默认给你 9×9 = 81 个交叉点的「9 路棋盘」用于入门——规则与 19 路完全一致，只是棋盘小、对局更快。"));
        ContentPanel.Children.Add(P("右侧下拉可把棋盘切到 13 或 19 路。下面的示例图都在 9 路棋盘上，每一格对应一个交叉点。"));
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
            "9 路棋盘 + 1 颗黑子在中上方（D 行 = GTP \"D6\"）。黑 ● 画在交叉点上，不是方格里。"
        ));
        ContentPanel.Children.Add(H2("谁先走"));
        ContentPanel.Children.Add(P("黑棋先走第一手；之后双方轮流，每次落一子或虚手（Pass）。一局的奇数手 = 黑下的，偶数手 = 白下的。"));
        ContentPanel.Children.Add(P("因为黑先手有微小优势，**中国规则下终局时白方获得「贴目」补偿**——通常是 7.5 目。中国围棋 用 KataGo 数子时会自动加这个贴目。"));

        ContentPanel.Children.Add(H2("基本术语速查"));
        var terms = new[]
        {
            "交叉点：棋盘上可以落子的点；9 路棋盘 81 个，19 路 361 个。",
            "气：一颗棋子上下左右相邻的空交叉点（详见第 2 章）。",
            "棋串 / 块：相邻同色棋子合并为一组，视为一个整体。",
            "提子：把对方无气的棋子从棋盘上移除（详见第 3 章）。",
            "目：中国规则下，「目」= 你围住的空交叉点数。终局时它 + 你的棋子数 = 你的分数。",
            "虚手（Pass）：本轮不下，让对方走。终局需要双方连续虚手才确认结束。",
            "星位：棋盘上预标记的常用开局点（详见第 7 章）。",
            "贴目：终局时给白方的额外加分（通常 7.5），补偿黑先手的小优势。",
        };
        foreach (var t in terms) ContentPanel.Children.Add(P("● " + t));

        ContentPanel.Children.Add(Note("这一章当作「术语词典」先读一遍，遇到不懂的术语回头查看；第 2~6 章结束后再回来会更清楚。"));
    }

    private void Chapter2_Liberty()
    {
        ContentPanel.Children.Add(H1("2. 气 · 棋子的呼吸"));
        ContentPanel.Children.Add(P("「气」是围棋的核心概念。一颗棋子上下左右相邻的「空交叉点」就是它的气。"));
        ContentPanel.Children.Add(P("棋串（一组上下左右相连的同色棋子）的气 = 整组邻接的所有空交叉点。**棋子必须有气，否则会被对手提掉**。"));

        ContentPanel.Children.Add(H2("中央 1 颗子 · 4 气"));
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
            "黑子落在 E5（中央）。它的 4 个邻位 (D5)(F5)(E6)(E4) 都是空交叉点 (! 标记)，所以它有 4 气。"
        ));

        ContentPanel.Children.Add(H2("边 1 颗子 · 3 气"));
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
            "黑子落在 A6（左边缘中部）。它左、下越界，所以只剩 3 个邻位：A7(上)、A5(下)、B6(右)，3 个都是空 (! 标记)，所以它有 3 气。"
        ));

        ContentPanel.Children.Add(H2("角落 1 颗子 · 2 气"));
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
            "黑子落在 A9（左下角）。它左、下都越界，所以只有 2 个有效邻位：A8(上) 和 B9(右)，2 个都是空 (! 标记)，所以它有 2 气。"
        ));

        ContentPanel.Children.Add(TipBox("金角银边草肚皮",
            "角落子气最少（2 气），最易守住；边上次之（3 气）；中间气最多（4 气）但最难守。" +
            " 实战经验是先占角、再走边、最后争中腹。"));

        ContentPanel.Children.Add(H2("棋串的气"));
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
            "黑子在 C5 和 D5 上下相连 = 同一棋串。它的气 = 整组邻接的所有空交叉点 = (C6)(D6)(E6)(E5)(C4) 共 5 个 (! 标记)。注意 (D6) 是 2 颗棋子的共享气，只算 1 个。"
        ));
    }

    private void Chapter3_Capture()
    {
        ContentPanel.Children.Add(H1("3. 提子 · 吃掉对方"));
        ContentPanel.Children.Add(P("一串对方棋子「无气」（0 个邻接空交叉点）时，会被立即从棋盘上移除，这就是「提子」。"));
        ContentPanel.Children.Add(P("判定顺序：你下一手棋时，系统先看这手棋「直接围死了哪些对方棋串」并立刻移除被吃掉的子；然后再看「你下的这颗子/形成的棋串」自己是否还有气（无气则是自杀手，详见第 4 章）。"));

        ContentPanel.Children.Add(H2("例子：角落白子被提"));
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
            "（提子前）当前局面：白 ○ 在 A9 角落。邻位 (B9)(A8) 中一个没下（即一个气 = ! 标记），其他邻位越界。白 ○ 还有 1 气。"
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
            "（提子瞬间）黑下 (A8)。现在白 ○ 的邻位 (B9) 黑、(A8) 黑 全占满，且左右越界。白 ○ 立即 0 气，被自动从棋盘上移除 → (A9) 位置变成空 (!)。"
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
            "（提子完成）黑 (A8)、(B9) 两子围住 (A9) 空位。这块空位以后终局数子时算黑棋的「目」。注意 中国围棋 **不需要你手动移除被吃的子**——它会自动执行围棋规则。"
        ));

        ContentPanel.Children.Add(TipBox("围棋提子判定两步",
            "1️⃣ **先看「我下这手棋直接围死了谁」**——会的话，被围死的对方死子立刻从棋盘清除。\n" +
            "2️⃣ **再看「我自己（含新形成的棋串）是否还有气」**——没气则是自杀手，禁（详见第 4 章）。"));
    }

    private void Chapter4_Suicide()
    {
        ContentPanel.Children.Add(H1("4. 自杀 · 禁止规则"));
        ContentPanel.Children.Add(P("你不能下一手让「己方这颗新子 + 它相连的所有己方同色棋子」整体 0 气（除非这手同时提掉对方某些子、让己方整体仍有气）。"));

        ContentPanel.Children.Add(H2("例子：自杀手（被 中国围棋 拒绝）"));
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
            "白想下 (J9) 救自己。看看会发生什么：(J9) 的 4 邻 = (J8) 黑、(I9 越界)、(K9 越界)、(J10 越界)。白下在这里自身 0 气，且这手没提任何对方子 → 自杀禁手，中国围棋 会拒绝并提示「KataGo 判定为非法手」。"
        ));

        ContentPanel.Children.Add(H2("例外：能提子的自杀形合法"));
        ContentPanel.Children.Add(NumberedBoard(
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "·········\n" +
            "····!····\n" +
            "····●····\n" +
            "··!○!····\n" +
            "··!○!····",
            "假设现在局面是这样：黑 (E3) 单独 1 子，邻位只剩 (D3)(F3)(D2)(F2) 4 个空（! = 4 气）。白想下 (E4)「扑」——看似自杀，但白下 (E4) 后：黑 (E3) 4 邻 = (D3)(F3)(D2)(F2)，里面多了 (E4)=白（不堵气），不对——这手其实不围黑。"
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
            "更标准的「扑」例子：2 颗孤立黑子 (D4)(D7) 各 1 气在 (D5)(D6)（! 标记）。白下 (D6) 看似自杀，但同时把 (D7) 的最后 1 气堵死 → (D7) 黑子 0 气被提，白 (D6) 自身还有气（与 (D5) 的潜在位相连），所以这手合法。\n" +
            "——这种手法叫「扑」，是高阶战术，初学阶段知道规则即可，实际遇到再学。"
        ));

        ContentPanel.Children.Add(TipBox("为什么围棋禁止自杀手？",
            "如果允许自杀，理论上一方可以一路把自己的子填进对方的空间——围棋就变成「谁能更快扔子」的游戏，与「围地盘」的本质背道而驰。\n" +
            "但围棋规则同时承认「能提子的自杀形」合法（扑 / 打劫），让高手能利用这点做战术——这是围棋深厚度的来源之一。"));
    }

    private void Chapter5_Ko()
    {
        ContentPanel.Children.Add(H1("5. 劫 · 全局禁提"));
        ContentPanel.Children.Add(P("围棋有一条看起来很奇怪的规则：**对方刚提了你一颗子，你不能立即在同一位置提回来**——必须先在别处下一手。"));
        ContentPanel.Children.Add(P("这条规则叫「劫禁」或「全局禁提」，它的作用是防止双方在同一位置反复提来提去、一局棋永远下不完。"));

        ContentPanel.Children.Add(H2("例子：黑白互提的循环"));
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
            "简化示意：黑 (E5) 与白 (E6) 上下挨着，黑 (E5) 1 邻接气在 (E4)（! 标记）。如果白下 (E4) 提掉黑 (E5)，黑不能立刻在同一位置 (E4) 下回来提白 (E4)——这是劫的雏形。真实围棋中劫的形状更加精细，但本质就是「双方共享 1 气 → 互相提」。"
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
            "实战劫形：白 4 子 (C4)(D4)(E5)(F5) 与黑 5 子 (E4)(F4)(G4)(G3)(G2) 缠斗，关键点 (G5) 唯一气位（!）。如果黑下 (G5) 提掉白一块，白不能立刻回 (G5) 提回——必须先去别处下一手（找「劫材」逼黑应一手）才能回来。"
        ));

        ContentPanel.Children.Add(TipBox("实战中劫的判断",
            "真正对局中识别劫后，双方会去找「劫材」——用别处的小损失强制对方应一手，把「劫」这个局部循环打开。围棋最高阶战术之一，初学阶段知道规则即可。"));
        ContentPanel.Children.Add(Note("中国围棋 完全遵守劫规则：你刚提一颗子，软件会拒掉你立刻在同一位置提回的尝试。被 KataGo 拒绝时，相信它、去别处下一手就行。"));
    }

    private void Chapter6_Endgame()
    {
        ContentPanel.Children.Add(H1("6. 终局判定 · 数子"));
        ContentPanel.Children.Add(P("对局什么时候结束？答：**双方连续虚手（Pass）**，双方都同意不再下了。这时用「数子」算出谁的得分更高。"));

        ContentPanel.Children.Add(H2("中国规则数子（中国围棋 默认）"));
        ContentPanel.Children.Add(P("终局时数子规则："));
        ContentPanel.Children.Add(P("● **黑棋得分 = 黑棋子数 + 黑棋围住的空交叉点数**"));
        ContentPanel.Children.Add(P("● **白棋得分 = 白棋子数 + 白棋围住的空交叉点数 + 7.5（贴目）**"));
        ContentPanel.Children.Add(P("得分多的一方胜。0.5 目避免和棋。"));

        ContentPanel.Children.Add(H2("「围住的空」是什么？"));
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
            "示意：黑满盘把白围死。但围棋不可能所有局面都这样极端——下面的图能更准确看到数子的细节。"
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
            "黑白各占一角：黑棋围住了 (F1)(F2)(F3) 三个空交叉点（只被白接触不到），这三个空算黑棋的「目」。白棋围住了 (E1)(E2)(E3) 三个空交叉点，算白棋的「目」。\n终局时，黑子数 + 黑空数 vs 白子数 + 白空数 + 7.5，谁多谁胜。"
        ));

        ContentPanel.Children.Add(H2("中国围棋 怎么做？"));
        ContentPanel.Children.Add(P("点右下角「📊 终局数子」按钮后程序自动："));
        ContentPanel.Children.Add(P("1. 双方各虚手一次（达成终局条件）"));
        ContentPanel.Children.Add(P("2. 调 KataGo 的 final_score 命令让它按中国规则自动判定"));
        ContentPanel.Children.Add(P("3. 弹出大字号胜负提示，例如「黑棋（你）胜 3.5 目」"));
        ContentPanel.Children.Add(P("4. 当前棋谱自动保存到桌面 + 加入棋谱库"));

        ContentPanel.Children.Add(Note("也可以直接点「🏳 认输」提前结束。如果读秒超时（主界面计时器），自动判当前执子方负。"));
    }

    private void Chapter7_Opening()
    {
        ContentPanel.Children.Add(H1("7. 实战入门 · 开局点"));
        ContentPanel.Children.Add(P("围棋有 9 个「星位」——传统上开局首子落在星位附近最稳。19 路棋盘的 9 个星位 GTP 坐标是："));
        ContentPanel.Children.Add(P("● **四角**：D4  D16  Q4  Q16"));
        ContentPanel.Children.Add(P("● **四边**：J4  J16  D10  Q10"));
        ContentPanel.Children.Add(P("● **中心（天元）**：J10"));

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
            "9 路棋盘的星位示意（仅标位置，具体实盘对局中以落子时棋盘上的 ★ 为准）：A9 / C9 / E5 / G3 / J1 等等——9 路比 19 路小，可用于教学演示。"
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
            "19 路棋盘示意（同样仅作位置说明）：★ 标记了 9 个星位——4 角 + 4 边中 + 中心天元。开局首子优先在这些点之一。"
        ));

        ContentPanel.Children.Add(H2("常见布局"));
        ContentPanel.Children.Add(P("● **星位开局**（★）：开局首子落星位，最稳，AI 首选。"));
        ContentPanel.Children.Add(P("● **小目开局**（★ 一侧偏一路）：偏向角部控制，常用于精细战斗。"));
        ContentPanel.Children.Add(P("● **三三开局**（角落深入一步）：抢占实地，但对方会从外侧攻击你的子，新手慎用。"));
        ContentPanel.Children.Add(P("● **天元**（中心）：先抢全局制高点，对手被迫先动，节奏感强。"));

        ContentPanel.Children.Add(TipBox("入门建议",
            "完全不知道下哪？跟着 KataGo 走——每步 AI 走完的回合一两秒它已经算完了，它下的就是当前局面下比较高概率的「好棋」。\n" +
            "走 5~10 局后你就会对围棋有个大致感觉，不用追求「最优」，享受围地盘的过程。"));

        ContentPanel.Children.Add(Note("最后：围棋的核心思想只有一句话——**「比对方围到更多地盘」**。所有规则（气、提子、自杀、劫、终局）都是为了把这句朴素的话变成可计算的规则。中国围棋 已经替你处理了所有规则细节，你只需要点选落子位置就行。"));
    }
}
