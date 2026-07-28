package main

import (
	"fmt"

	"github.com/charmbracelet/bubbles/progress"
	"github.com/charmbracelet/bubbles/spinner"
	"github.com/charmbracelet/bubbles/viewport"
	tea "github.com/charmbracelet/bubbletea"
	"github.com/charmbracelet/huh"
	"github.com/charmbracelet/lipgloss"
)

type state int

const (
	stateLoading        state = iota
	stateSelectVersion
	stateDownloading
	stateExtracting
	stateAskShortcut
	stateCreatingShortcut
	stateDone
	stateError
)

type releasesFetchedMsg struct {
	releases []string
}

type downloadProgressMsg float64

type downloadCompleteMsg struct {
	path string
}

type extractCompleteMsg struct {
	installDir string
}

type shortcutCreatedMsg struct{}

type logMsg struct {
	line string
}

type errMsg struct {
	err error
}

type model struct {
	state           state
	tag             string
	progress        float64
	err             error
	zipPath         string
	releases        []string
	spinner         spinner.Model
	progressBar     progress.Model
	viewport        viewport.Model
	logs            []string
	width           int
	height          int
	customTag       string
	customPath      string
	installDir      string
	versionForm     *huh.Form
	shortcutForm    *huh.Form
	progressCh      chan float64
	stepIndex       int
	totalSteps      int
	selectedVersion string
	createShortcut  bool
	downloadedBytes int64
	totalBytes      int64
}

func initialModel(customTag, customPath string) model {
	s := spinner.New()
	s.Spinner = spinner.Dot
	s.Style = lipgloss.NewStyle().Foreground(lipgloss.Color("#6C5CE7"))

	pb := progress.New(
		progress.WithDefaultGradient(),
		progress.WithoutPercentage(),
	)

	vp := viewport.New(80, 8)
	vp.Style = logViewportStyle

	totalSteps := 5
	startState := stateLoading
	if customTag != "" {
		totalSteps = 4
		startState = stateDownloading
	}

	return model{
		state:       startState,
		spinner:     s,
		progressBar: pb,
		viewport:    vp,
		logs:        []string{},
		customTag:   customTag,
		customPath:  customPath,
		totalSteps:  totalSteps,
		stepIndex:   0,
	}
}

func (m model) Init() tea.Cmd {
	if m.customTag != "" {
		m.logs = append(m.logs, "🚀 FluxDB Installer gestartet")
		m.logs = append(m.logs, fmt.Sprintf("📌 Version vorgegeben: %s", m.customTag))
		return tea.Batch(
			m.spinner.Tick,
			func() tea.Msg {
				return tagFetchedMsg{tag: m.customTag}
			},
		)
	}

	return tea.Batch(
		m.spinner.Tick,
		func() tea.Msg {
			return logMsg{line: "🚀 FluxDB Installer gestartet"}
		},
		func() tea.Msg {
			return logMsg{line: "📡 Rufe GitHub Releases ab..."}
		},
		fetchReleasesCmd(),
	)
}

func (m *model) addLog(line string) {
	m.logs = append(m.logs, line)
	m.viewport.SetContent(formatLogs(m.logs))
	m.viewport.GotoBottom()
}

func (m *model) updateViewport() {
	m.viewport.SetContent(formatLogs(m.logs))
	m.viewport.GotoBottom()
}

type tagFetchedMsg struct {
	tag string
}