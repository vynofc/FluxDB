package main

import (
	"os"
	"strings"
	"time"
	"unicode"

	"github.com/charmbracelet/bubbles/viewport"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/lipgloss"
	"github.com/charmbracelet/log"
)

const version = "v1.0.1"

var logStyles = func() *log.Styles {
	s := log.DefaultStyles()
	s.Timestamp = lipgloss.NewStyle().Faint(true)
	return s
}()

type model struct {
	logPath     string
	lines       []string
	styledLines []string
	viewport    viewport.Model
	lastSize    int64
	ready       bool
	err         error
}

func initialModel(logPath string) model {
	vp := viewport.New(80, 24)
	vp.Style = lipgloss.NewStyle()

	return model{
		logPath:  logPath,
		viewport: vp,
		lastSize: -1,
	}
}

func (m *model) Init() tea.Cmd {
	return tea.Batch(loadLogCmd(m.logPath), tailTick())
}

func (m *model) setLines(msg []string) {
	m.lines = msg
	m.styledLines = make([]string, len(msg))
	for i, line := range msg {
		m.styledLines[i] = styleLogLine(line)
	}
	m.lastSize = -1
	if info, err := os.Stat(m.logPath); err == nil {
		m.lastSize = info.Size()
	}
	m.ready = true
	m.viewport.SetContent(strings.Join(m.styledLines, "\n"))
}

func styleLogLine(raw string) string {
	level, ts, prefix, message := parseLogLine(raw)

	var sb strings.Builder
	if ts != "" {
		sb.WriteString(logStyles.Timestamp.Render(ts))
		sb.WriteByte(' ')
	}
	sb.WriteString(logStyles.Levels[level].Render(level.String()))
	sb.WriteByte(' ')
	if prefix != "" {
		sb.WriteString(logStyles.Prefix.Render(prefix + ":"))
		sb.WriteByte(' ')
	}
	sb.WriteString(message)
	return sb.String()
}

func containsWord(s string, words ...string) bool {
	for _, field := range strings.FieldsFunc(s, func(r rune) bool {
		return !unicode.IsLetter(r) && !unicode.IsDigit(r)
	}) {
		for _, w := range words {
			if field == w {
				return true
			}
		}
	}
	return false
}

func parseLogLine(raw string) (log.Level, string, string, string) {
	level := log.InfoLevel
	ts := ""
	prefix := ""
	message := ""
	rest := raw

	if strings.HasPrefix(rest, "[") {
		if idx := strings.IndexByte(rest, ']'); idx > 0 && idx+1 < len(rest) {
			ts = rest[1:idx]
			rest = strings.TrimSpace(rest[idx+1:])
		}
	}

	lower := strings.ToLower(rest)
	if containsWord(lower, "error", "fehler", "exception") {
		level = log.ErrorLevel
	} else if containsWord(lower, "warn", "warning") {
		level = log.WarnLevel
	} else if containsWord(lower, "debug", "trace") {
		level = log.DebugLevel
	}

	if idx := strings.IndexByte(rest, ':'); idx > 0 && idx < len(rest)-1 {
		candidate := rest[:idx]
		if !strings.ContainsAny(candidate, "/\\") && len(candidate) <= 40 {
			prefix = candidate
			rest = strings.TrimSpace(rest[idx+1:])
		}
	}

	message = rest
	return level, ts, prefix, message
}

func (m model) View() string {
	if m.err != nil {
		errStyle := lipgloss.NewStyle().Foreground(lipgloss.Color("#E81123")).Bold(true)
		return appStyle.Render(titleStyle.Render("FluxDB Log Viewer "+version) + "\n\n" +
			errStyle.Render("Fehler: "+m.err.Error()) + "\n\n" +
			helpStyle.Render("R Neu laden  |  Esc/Q Beenden"))
	}
	if !m.ready {
		return appStyle.Render(titleStyle.Render("FluxDB Log Viewer "+version) + "\n\n" + dimStyle.Render("Lade..."))
	}

	header := titleStyle.Render("FluxDB Log Viewer "+version) + "  " + dimStyle.Render(m.logPath)
	body := m.viewport.View()
	footer := helpStyle.Render("↑↓ Scrollen  |  H Oben  |  B Unten  |  R Neu laden  |  Esc/Q Beenden")

	return appStyle.Render(lipgloss.JoinVertical(lipgloss.Top, header, body, footer))
}

func loadLogCmd(path string) tea.Cmd {
	return func() tea.Msg {
		data, err := os.ReadFile(path)
		if err != nil {
			return errMsg{err}
		}
		content := strings.TrimRight(string(data), "\n")
		if content == "" {
			return logLinesMsg(nil)
		}
		return logLinesMsg(strings.Split(content, "\n"))
	}
}

func tailTick() tea.Cmd {
	return tea.Tick(500*time.Millisecond, func(time.Time) tea.Msg { return tailTickMsg{} })
}
