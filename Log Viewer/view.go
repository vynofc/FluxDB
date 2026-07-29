package main

import (
	"fmt"
	"os"
	"strings"
	"time"

	"github.com/charmbracelet/bubbles/viewport"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/lipgloss"
)

type model struct {
	state        state
	logPath      string
	logLines     []string
	viewport     viewport.Model
	width        int
	height       int
	searchTerm   string
	searchInput  string
	searchIdx    int
	matchedLines []int
	lastSize     int64
	ready        bool
}

func initialModel(logPath string) model {
	vp := viewport.New(80, 20)
	vp.Style = lipgloss.NewStyle().
		Border(lipgloss.RoundedBorder()).
		BorderForeground(primaryColor).
		Padding(0, 1)

	return model{
		state:    stateViewing,
		logPath:  logPath,
		logLines: []string{},
		viewport: vp,
		lastSize: -1,
	}
}

func (m *model) Init() tea.Cmd {
	return tea.Batch(
		loadLogFileCmd(m.logPath),
		tailTickCmd(),
	)
}

func (m *model) updateViewportContent() {
	m.viewport.SetContent(m.renderLogLines())
	m.viewport.GotoBottom()
}

func (m *model) renderLogLines() string {
	if m.searchTerm != "" {
		return m.renderSearchHighlighted()
	}

	var sb strings.Builder
	for _, line := range m.logLines {
		sb.WriteString(line)
		sb.WriteString("\n")
	}
	return sb.String()
}

func (m *model) renderSearchHighlighted() string {
	var sb strings.Builder
	lowerTerm := strings.ToLower(m.searchTerm)

	for i, line := range m.logLines {
		lowerLine := strings.ToLower(line)
		idx := strings.Index(lowerLine, lowerTerm)
		if idx < 0 {
			sb.WriteString(dimStyle.Render(line))
			sb.WriteString("\n")
			continue
		}

		isCurrentMatch := false
		if len(m.matchedLines) > 0 && m.searchIdx < len(m.matchedLines) {
			isCurrentMatch = m.matchedLines[m.searchIdx] == i
		}

		highlightStyle := searchMatchStyle
		if isCurrentMatch {
			highlightStyle = searchCurrentMatchStyle
		}

		sb.WriteString(dimStyle.Render(line[:idx]))
		sb.WriteString(highlightStyle.Render(line[idx : idx+len(m.searchTerm)]))
		sb.WriteString(dimStyle.Render(line[idx+len(m.searchTerm):]))
		sb.WriteString("\n")
	}
	return sb.String()
}

func (m model) View() string {
	if !m.ready {
		return titleStyle.Render("FluxDB Log Viewer") + "\n\n" + dimStyle.Render("Loading...")
	}

	var sb strings.Builder

	header := titleStyle.Render("FluxDB Log Viewer")
	if m.logPath != "" {
		header += "  " + dimStyle.Render(m.logPath)
	}
	sb.WriteString(header)
	sb.WriteString("\n\n")

	sb.WriteString(m.viewport.View())
	sb.WriteString("\n\n")

	if m.state == stateSearching {
		sb.WriteString(searchStyle.Render("/"))
		sb.WriteString(searchInputStyle.Render(m.searchInput))
		if m.searchTerm != "" {
			info := fmt.Sprintf("  (%d/%d matches)", m.searchIdx+1, len(m.matchedLines))
			sb.WriteString(dimStyle.Render(info))
		}
		sb.WriteString("\n\n")
	}

	sb.WriteString(m.renderFooter())

	return lipgloss.NewStyle().
		Padding(1, 2).
		Render(sb.String())
}

func (m model) renderFooter() string {
	if m.state == stateSearching {
		return helpStyle.Render("Enter = Next  |  Esc = Cancel  |  Ctrl+C/Q = Quit")
	}
	return helpStyle.Render("PgUp/PgDn = Scroll  |  Home/End = Top/Bottom  |  / = Search  |  N = Next match  |  R = Reload  |  C = Clear  |  Esc/Q = Quit")
}

func loadLogFileCmd(path string) tea.Cmd {
	return func() tea.Msg {
		data, err := os.ReadFile(path)
		if err != nil {
			return errMsg{err}
		}
		content := string(data)
		var lines []string
		if content != "" {
			lines = strings.Split(strings.TrimRight(content, "\n"), "\n")
		}
		return logLinesMsg(lines)
	}
}

func clearLogFileCmd(path string) tea.Cmd {
	return func() tea.Msg {
		err := os.WriteFile(path, []byte{}, 0644)
		if err != nil {
			return errMsg{err}
		}
		return logLinesMsg([]string{})
	}
}

func tailTickCmd() tea.Cmd {
	return tea.Tick(500*time.Millisecond, func(t time.Time) tea.Msg {
		return tailTickMsg{}
	})
}