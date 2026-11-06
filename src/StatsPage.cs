using System.Text;

namespace StudentTimerClock;

/// <summary>Statistics: graphs of how long you spent on each named timer / stopwatch / lap.</summary>
public class StatsPage : UserControl
{
    readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(120) };
    readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(215) };
    readonly DataGridView _totals = new();
    readonly ComboBox _by = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(100) };
    readonly ComboBox _name = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(150) };
    readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(90) };
    readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = Theme.S(100) };
    readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = Theme.S(100) };
    readonly ChartControl _chart = new() { Dock = DockStyle.Fill };
    readonly DataGridView _grid = new();
    readonly Label _summary = new() { AutoSize = false, Dock = DockStyle.Top, Height = Theme.S(30), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Theme.S(8), 0, 0, 0), BackColor = Color.FromArgb(250, 251, 252) };
    bool _loading;
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
    Label _dash;
    List<SessionRecord> _current = new();

    static readonly string[] Periods = { "Today", "Yesterday", "This week", "Last 7 days", "This month", "Last 30 days", "This year", "All time", "Custom…" };
    const string KAll = "Timers + stopwatches + lap names";
    static readonly string[] Kinds = { KAll, "Timers + stopwatches (no laps)", "Stopwatch lap names only", "Stopwatches only", "Timers only" };

    public StatsPage()
    {
        Font = Theme.Normal;
        BackColor = Color.White;
        _loading = true;
        _period.Items.AddRange(Periods); _period.SelectedIndex = 3;
        _kind.Items.AddRange(Kinds); _kind.SelectedIndex = 0;
        _by.Items.AddRange(new object[] { "Name", "Group", "Type", "Day" }); _by.SelectedIndex = 0;
        _type.Items.AddRange(new object[] { "Bar", "Pie" }); _type.SelectedIndex = 0;
        _from.Value = DateTime.Today.AddDays(-7); _to.Value = DateTime.Today;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(Theme.S(6), Theme.S(6), Theme.S(6), Theme.S(2)), BackColor = Theme.HeaderBack, WrapContents = true };
        void Add(string label, Control c)
        {
            if (label != null)
            {
                var l = new Label { Text = label, AutoSize = true, Margin = new Padding(Theme.S(6), Theme.S(7), Theme.S(2), 0) };
                if (label == "–") _dash = l;
                bar.Controls.Add(l);
            }
            c.Margin = new Padding(0, Theme.S(3), Theme.S(6), Theme.S(3));
            bar.Controls.Add(c);
        }
        Add("Period:", _period); Add(null, _from); Add("–", _to);
        Add("Show:", _kind); Add("Group by:", _by); Add("Only:", _name); Add("Chart:", _type);
        foreach (var c in new Control[] { _period, _kind, _by, _name, _type }) ((ComboBox)c).SelectedIndexChanged += (s, e) => { if (!_loading) RefreshData(); };
        _from.ValueChanged += (s, e) => { if (!_loading) RefreshData(); };
        _to.ValueChanged += (s, e) => { if (!_loading) RefreshData(); };

        // grids
        foreach (var gv in new[] { _grid, _totals })
        {
            gv.Dock = DockStyle.Fill;
            gv.ReadOnly = true; gv.AllowUserToAddRows = false; gv.AllowUserToDeleteRows = false; gv.AllowUserToResizeRows = false;
            gv.RowHeadersVisible = false; gv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gv.BackgroundColor = Color.White; gv.BorderStyle = BorderStyle.None;
            gv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gv.EnableHeadersVisualStyles = false;
            gv.ColumnHeadersDefaultCellStyle.BackColor = Theme.HeaderBack;
            gv.ColumnHeadersDefaultCellStyle.Font = Theme.Bold;
            gv.DefaultCellStyle.SelectionBackColor = Theme.AccentLight;
            gv.DefaultCellStyle.SelectionForeColor = Color.Black;
            gv.GridColor = Theme.GridLine;
            gv.RowTemplate.Height = Theme.S(24);
        }
        foreach (var (n, w) in new[] { ("Date", 90), ("Start", 70), ("End", 70), ("Name", 160), ("Type", 80), ("Group / Stopwatch", 130), ("Duration", 90) })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = n, FillWeight = w, SortMode = DataGridViewColumnSortMode.Automatic });
        foreach (var (n, w) in new[] { ("Name", 170), ("Type", 150), ("From", 170), ("Sessions", 70), ("Total time", 100), ("Share", 70) })
            _totals.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = n, FillWeight = w, SortMode = DataGridViewColumnSortMode.NotSortable });
        _totals.Columns[3].DefaultCellStyle.Alignment = _totals.Columns[4].DefaultCellStyle.Alignment = _totals.Columns[5].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _totals.Columns[4].DefaultCellStyle.Font = Theme.Bold;
        // double-click a total -> show only that name
        _totals.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0 || _totals.Rows[e.RowIndex].Tag is not string n || (string)_by.SelectedItem != "Name") return;
            if (_name.Items.Contains(n)) { _name.SelectedItem = n; _tabs.SelectedIndex = 1; }
        };

        var gridBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(Theme.S(6)), BackColor = Theme.HeaderBack };
        Button B(string t, string icon, Action a)
        {
            var b = new Button { Text = " " + t, AutoSize = true, Height = Theme.S(28), Image = icon == null ? null : Icons.Get(icon, Theme.S(16)), TextImageRelation = TextImageRelation.ImageBeforeText };
            b.Click += (s, e) => a();
            gridBar.Controls.Add(b);
            return b;
        }
        B("Export CSV…", "export", ExportCsv);
        B("Save chart image…", "report", () => LapReportForm.SaveImage(_chart, FindForm(), "Student Timer Clock statistics"));
        B("Copy chart", "clipboard", () => { using var bmp = _chart.Snapshot(); Clipboard.SetImage(bmp); });
        B("Delete selected sessions", "delete", DeleteSelected);
        B("Clear all history…", null, ClearAll);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = Theme.S(6), BackColor = Theme.Border };
        split.Panel1.BackColor = Color.White; split.Panel2.BackColor = Color.White;
        split.Panel1.Controls.Add(_chart);
        var tTotals = new TabPage("Totals") { BackColor = Color.White };
        var tSessions = new TabPage("Sessions") { BackColor = Color.White };
        tTotals.Controls.Add(_totals);
        tSessions.Controls.Add(_grid);
        _tabs.TabPages.Add(tTotals); _tabs.TabPages.Add(tSessions);
        split.Panel2.Controls.Add(_tabs);
        split.Panel2.Controls.Add(gridBar);
        Controls.Add(split);
        Controls.Add(_summary);
        Controls.Add(bar);
        bool splitSet = false;
        void FixSplit()
        {
            try
            {
                if (split.Height < 250) return;
                if (!splitSet || split.SplitterDistance < 80 || split.Height - split.SplitterDistance < Theme.S(150))
                {
                    split.SplitterDistance = Math.Max(80, (int)(split.Height * 0.52));
                    splitSet = true;
                }
            }
            catch { }
        }
        split.SizeChanged += (s, e) => FixSplit();
        VisibleChanged += (s, e) => { if (Visible) FixSplit(); };
        _loading = false;
        UpdateDateVisibility();
    }

    void UpdateDateVisibility()
    {
        bool custom = (string)_period.SelectedItem == "Custom…";
        _from.Visible = _to.Visible = custom;
        if (_dash != null) _dash.Visible = custom;
    }

    (DateTime fromLocal, DateTime toLocal) Range()
    {
        var today = DateTime.Today;
        switch ((string)_period.SelectedItem)
        {
            case "Today": return (today, today.AddDays(1));
            case "Yesterday": return (today.AddDays(-1), today);
            case "This week":
                int diff = ((int)today.DayOfWeek - (int)System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
                return (today.AddDays(-diff), today.AddDays(1));
            case "Last 7 days": return (today.AddDays(-6), today.AddDays(1));
            case "This month": return (new DateTime(today.Year, today.Month, 1), today.AddDays(1));
            case "Last 30 days": return (today.AddDays(-29), today.AddDays(1));
            case "This year": return (new DateTime(today.Year, 1, 1), today.AddDays(1));
            case "Custom…": return (_from.Value.Date, _to.Value.Date.AddDays(1));
            default: return (DateTime.MinValue, DateTime.MaxValue);
        }
    }

    IEnumerable<SessionRecord> AllSessionsIncludingLive()
    {
        foreach (var h in AppState.Data.History) yield return h;
        var now = DateTime.UtcNow;
        foreach (var it in AppState.AllItems.Where(i => i.IsRunning && i.StartedAtUtc.HasValue))
        {
            if (it is TimerItem tt && tt.IsTarget) continue; // exam countdowns are not "time spent"
            yield return new SessionRecord { Id = "live", Name = it.Name, Kind = it.Kind.ToString(), Group = it.Group, StartUtc = it.StartedAtUtc.Value, EndUtc = now, Seconds = (now - it.StartedAtUtc.Value).TotalSeconds, ColorArgb = it.ColorArgb };
        }
        // the lap that is in progress right now (running or paused) counts under its title too
        foreach (var sw in AppState.Data.Stopwatches)
        {
            double cur = sw.CurrentLap(now);
            if (cur < 1) continue;
            string name = string.IsNullOrWhiteSpace(sw.CurrentLapName) ? "Lap " + (sw.Laps.Count + 1) : sw.CurrentLapName.Trim();
            yield return new SessionRecord { Id = "live", Name = name, Kind = "Lap", Group = sw.Group, Parent = sw.Name, StartUtc = now.AddSeconds(-cur), EndUtc = now, Seconds = cur, ColorArgb = sw.ColorArgb };
        }
    }

    /// <summary>
    /// Timers + stopwatches + lap titles without counting anything twice: a stopwatch that has laps is replaced by its laps
    /// (per day); any stopwatch time not covered by a lap is shown as "Stopwatch (no lap title)".
    /// </summary>
    static List<SessionRecord> SplitStopwatchesIntoLaps(List<SessionRecord> q)
    {
        var result = q.Where(h => h.Kind != "Stopwatch").ToList();
        foreach (var g in q.Where(h => h.Kind == "Stopwatch").GroupBy(h => (h.Name, Day: h.StartUtc.ToLocalTime().Date)))
        {
            double lapSecs = q.Where(h => h.Kind == "Lap" && h.Parent == g.Key.Name && h.StartUtc.ToLocalTime().Date == g.Key.Day).Sum(h => h.Seconds);
            if (lapSecs <= 0) { result.AddRange(g); continue; }
            double rest = g.Sum(h => h.Seconds) - lapSecs;
            if (rest < 1) continue;
            var first = g.First();
            result.Add(new SessionRecord
            {
                Id = "rest", Name = first.Name + " (no lap title)", Kind = "Stopwatch", Group = first.Group,
                StartUtc = g.Key.Day.ToUniversalTime(), EndUtc = g.Key.Day.ToUniversalTime(), Seconds = rest, ColorArgb = first.ColorArgb
            });
        }
        return result;
    }

    static string TypeText(SessionRecord h) => h.Kind == "Lap" ? "Stopwatch lap" : h.Id == "rest" ? "Stopwatch (rest)" : h.Kind;

    public void RefreshData()
    {
        if (_loading) return;
        UpdateDateVisibility();
        var (f, t) = Range();
        var fu = f == DateTime.MinValue ? DateTime.MinValue : f.ToUniversalTime();
        var tu = t == DateTime.MaxValue ? DateTime.MaxValue : t.ToUniversalTime();
        string kind = (string)_kind.SelectedItem;
        var q = AllSessionsIncludingLive().Where(h => h.StartUtc < tu && h.EndUtc >= fu);
        bool lapsOnly = kind == "Stopwatch lap names only";
        var basis = kind switch
        {
            "Stopwatches only" => q.Where(h => h.Kind == "Stopwatch").ToList(),
            "Timers only" => q.Where(h => h.Kind == "Timer").ToList(),
            "Stopwatch lap names only" => q.Where(h => h.Kind == "Lap").ToList(),
            "Timers + stopwatches (no laps)" => q.Where(h => h.Kind != "Lap").ToList(),
            _ => SplitStopwatchesIntoLaps(q.ToList()),
        };

        // names filter
        string selName = _name.SelectedItem as string;
        var names = basis.Select(h => h.Name).Distinct().OrderBy(n => n).ToList();
        _loading = true;
        _name.Items.Clear(); _name.Items.Add("(all)"); _name.Items.AddRange(names.Cast<object>().ToArray());
        _name.SelectedItem = selName != null && names.Contains(selName) ? selName : "(all)";
        _loading = false;
        if ((string)_name.SelectedItem != "(all)") basis = basis.Where(h => h.Name == (string)_name.SelectedItem).ToList();
        _current = basis;

        // chart
        string by = (string)_by.SelectedItem;
        var pts = new List<ChartPoint>();
        int ci = 0;
        Color ColorFor(IEnumerable<SessionRecord> g)
        {
            var c = g.Select(x => x.ColorArgb).FirstOrDefault(x => x != 0);
            var pastel = Color.FromArgb(c);
            bool distinct = c != 0 && pastel.GetSaturation() > 0.15f && pts.All(p => p.Color != Theme.Saturate(pastel));
            return distinct ? Theme.Saturate(pastel) : Theme.ChartColors[ci % Theme.ChartColors.Length];
        }
        if (by == "Day")
        {
            var start = f == DateTime.MinValue ? (basis.Count > 0 ? basis.Min(b => b.StartUtc).ToLocalTime().Date : DateTime.Today) : f;
            var end = t == DateTime.MaxValue ? DateTime.Today.AddDays(1) : t;
            if ((end - start).TotalDays > 62) start = end.AddDays(-62);
            for (var d = start; d < end; d = d.AddDays(1))
            {
                var day = d;
                var secs = basis.Where(h => h.StartUtc.ToLocalTime().Date == day).Sum(h => h.Seconds);
                pts.Add(new ChartPoint { Label = d.ToString("ddd d"), Value = secs, Color = Theme.ChartColors[0], Count = basis.Count(h => h.StartUtc.ToLocalTime().Date == day) });
            }
        }
        else
        {
            foreach (var g in basis.GroupBy(h => KeyOf(h, by, lapsOnly)).OrderByDescending(g => g.Sum(x => x.Seconds)))
            {
                pts.Add(new ChartPoint { Label = g.Key, Value = g.Sum(x => x.Seconds), Color = ColorFor(g), Count = g.Count() });
                ci++;
            }
        }
        _chart.Type = (string)_type.SelectedItem == "Pie" && by != "Day" ? ChartType.Pie : ChartType.Bar;
        _chart.Title = $"Time spent - {(string)_period.SelectedItem}".Replace("Custom…", $"{f:d} – {t.AddDays(-1):d}");
        _chart.Subtitle = $"{kind}  •  grouped by {by.ToLower()}" + ((string)_name.SelectedItem != "(all)" ? $"  •  {(string)_name.SelectedItem}" : "");
        _chart.SetData(pts);

        double total = basis.Sum(h => h.Seconds);
        var top = pts.Where(p => by != "Day").OrderByDescending(p => p.Value).FirstOrDefault();
        _summary.Text = $"Total: {Fmt.Human(total)}    •    Sessions: {basis.Count}" + (top != null ? $"    •    Most time: {top.Label} ({Fmt.Human(top.Value)})" : "") + (basis.Any(b => b.Id == "live") ? "    •    includes running items (live)" : "");

        // totals table (same grouping as the chart; for "Day" grouping list by name)
        _totals.SuspendLayout();
        _totals.Rows.Clear();
        string tby = by == "Day" ? "Name" : by;
        _totals.Columns[0].HeaderText = tby;
        foreach (var g in basis.GroupBy(h => KeyOf(h, tby, lapsOnly)).OrderByDescending(g => g.Sum(x => x.Seconds)))
        {
            double secs = g.Sum(x => x.Seconds);
            string types = string.Join(", ", g.Select(TypeText).Distinct().OrderBy(x => x));
            string from = string.Join(", ", g.Select(h => h.Kind == "Lap" ? h.Parent : h.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).Take(6));
            int i = _totals.Rows.Add(g.Key, types, tby == "Name" && !g.Any(h => h.Kind == "Lap") ? g.First().Group : from, g.Count(h => h.Id != "rest"), Fmt.Time(secs, false, true), total > 0 ? $"{secs / total * 100:0.#}%" : "");
            _totals.Rows[i].Tag = g.Key;
            var c = g.Select(x => x.ColorArgb).FirstOrDefault(x => x != 0);
            if (c != 0) _totals.Rows[i].Cells[0].Style.BackColor = Color.FromArgb(c);
        }
        if (basis.Count > 0)
        {
            int ti = _totals.Rows.Add("TOTAL", "", "", basis.Count(h => h.Id != "rest"), Fmt.Time(total, false, true), "100%");
            _totals.Rows[ti].DefaultCellStyle.Font = Theme.Bold;
            _totals.Rows[ti].DefaultCellStyle.BackColor = Theme.HeaderBack;
        }
        _totals.ResumeLayout();
        _totals.Columns[2].HeaderText = tby == "Name" ? "Stopwatch / group" : "Items";

        // sessions grid
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var h in basis.OrderByDescending(h => h.StartUtc).Take(3000))
        {
            var s = h.StartUtc.ToLocalTime(); var e = h.EndUtc.ToLocalTime();
            bool rest = h.Id == "rest";
            int i = _grid.Rows.Add(s.ToString("d"), rest ? "" : Fmt.Clock(s), rest ? "" : h.Id == "live" ? "running…" : Fmt.Clock(e), h.Name, TypeText(h), h.Kind == "Lap" ? h.Parent : h.Group, Fmt.Time(h.Seconds, h.Seconds < 60, false));
            _grid.Rows[i].Tag = h;
            if (h.ColorArgb != 0) _grid.Rows[i].Cells[3].Style.BackColor = Color.FromArgb(h.ColorArgb);
        }
        _grid.ResumeLayout();
    }

    static string KeyOf(SessionRecord h, string by, bool lapsOnly)
    {
        switch (by)
        {
            case "Group":
                var g = lapsOnly ? h.Parent : h.Group;
                return string.IsNullOrWhiteSpace(g) ? "General" : g;
            case "Type":
                return h.Kind == "Lap" ? "Lap: " + h.Parent : h.Kind == "Timer" ? "Timers" : "Stopwatches";
            default:
                return h.Name;
        }
    }

    void DeleteSelected()
    {
        var sel = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as SessionRecord).Where(r => r != null && r.Id != "live" && r.Id != "rest").ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show(FindForm(), $"Delete {sel.Count} session(s) from history?", "Student Timer Clock", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        foreach (var s in sel) AppState.Data.History.Remove(s);
        AppState.Save();
        RefreshData();
    }

    void ClearAll()
    {
        if (MessageBox.Show(FindForm(), "Delete ALL recorded history? This cannot be undone.", "Student Timer Clock", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        AppState.Data.History.Clear();
        AppState.Save();
        RefreshData();
    }

    void ExportCsv()
    {
        using var sfd = new SaveFileDialog { Filter = "CSV file|*.csv", FileName = $"StudentTimerClock-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (sfd.ShowDialog(FindForm()) != DialogResult.OK) return;
        var sb = new StringBuilder();
        static string Q(string s) => Security.InputValidator.CsvField(s); // quoted + protected against spreadsheet formula injection
        sb.AppendLine("Date,Start,End,Name,Type,Group,Parent,Seconds,Duration");
        foreach (var h in _current.OrderBy(h => h.StartUtc))
        {
            var s = h.StartUtc.ToLocalTime(); var e = h.EndUtc.ToLocalTime();
            sb.AppendLine(string.Join(",", s.ToString("yyyy-MM-dd"), s.ToString("HH:mm:ss"), e.ToString("HH:mm:ss"), Q(h.Name), h.Kind, Q(h.Group), Q(h.Parent), h.Seconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Fmt.Time(h.Seconds)));
        }
        File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));
    }
}
