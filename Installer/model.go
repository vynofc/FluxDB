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

type step struct {
	title string
	done  bool
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
	steps           []step
	activeStep      int
	selectedVersion string
	createShortcut  bool
	detail          bool
}

func initialModel(customTag, customPath string, detail bool) model {
	s := spinner.New()
	s.Spinner = spinner.Dot
	s.Style = lipgloss.NewStyle().Foreground(lipgloss.Color("#6C5CE7"))

	pb := progress.New(
		progress.WithDefaultGradient(),
		progress.WithoutPercentage(),
	)

	vp := viewport.New(80, 8)
	vp.Style = logViewportStyle

	var steps []step
	var startState state

	if customTag != "" {
		steps = []step{
			{title: "Download"},
			{title: "Entpacken"},
			{title: "Abschluss"},
		}
		startState = stateDownloading
	} else if detail {
		steps = []step{
			{title: "Releases abrufen"},
			{title: "Version waehlen"},
			{title: "Download"},
			{title: "Entpacken"},
			{title: "Abschluss"},
		}
		startState = stateLoading
	} else {
		steps = []step{
			{title: "Version ermitteln"},
			{title: "Download"},
			{title: "Entpacken"},
			{title: "Abschluss"},
		}
		startState = stateLoading
	}

	return model{
		state:       startState,
		spinner:     s,
		progressBar: pb,
		viewport:    vp,
		logs:        []string{},
		customTag:   customTag,
		customPath:  customPath,
		steps:       steps,
		activeStep:  0,
		detail:      detail,
	}
}

func (m *model) Init() tea.Cmd {
	if m.customTag != "" {
		if m.detail {
			m.addLog("🚀 FluxDB Installer gestartet")
			m.addLog(fmt.Sprintf("📌 Version vorgegeben: %s", m.customTag))
		}
		return tea.Batch(
			m.spinner.Tick,
			func() tea.Msg { return tagFetchedMsg{tag: m.customTag} },
		)
	}

	if m.detail {
		return tea.Batch(
			m.spinner.Tick,
			func() tea.Msg { return logMsg{line: "🚀 FluxDB Installer gestartet"} },
			func() tea.Msg { return logMsg{line: "📡 Rufe GitHub Releases ab..."} },
			fetchReleasesCmd(),
		)
	}

	return tea.Batch(
		m.spinner.Tick,
		fetchLatestTagCmd(),
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

func (m *model) nextStep() {
	if m.activeStep < len(m.steps)-1 {
		m.steps[m.activeStep].done = true
		m.activeStep++
	}
}

func (m *model) markAllStepsDone() {
	m.steps[m.activeStep].done = true
}

type tagFetchedMsg struct {
	tag string
}