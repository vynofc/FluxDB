package main

import (
	"github.com/charmbracelet/lipgloss"
)

var (
	primaryColor   = lipgloss.Color("#6C5CE7")
	successColor   = lipgloss.Color("#00B894")
	errorColor     = lipgloss.Color("#FF7675")
	textColor      = lipgloss.Color("#DFE6E9")
	dimColor       = lipgloss.Color("#636E72")
	highlightColor = lipgloss.Color("#FFEAA7")

	titleStyle = lipgloss.NewStyle().
			Foreground(primaryColor).
			Bold(true).
			MarginBottom(1)

	statusStyle = lipgloss.NewStyle().
			Foreground(textColor)

	successStyle = lipgloss.NewStyle().
			Foreground(successColor).
			Bold(true)

	errorStyle = lipgloss.NewStyle().
			Foreground(errorColor).
			Bold(true)

	dimStyle = lipgloss.NewStyle().
			Foreground(dimColor)

	helpStyle = lipgloss.NewStyle().
			Foreground(dimColor).
			MarginTop(1)

	appStyle = lipgloss.NewStyle().
			Padding(1, 2)

	logViewportStyle = lipgloss.NewStyle().
				Border(lipgloss.RoundedBorder()).
				BorderForeground(primaryColor).
				Padding(0, 1).
				MarginTop(1)

	logStyle = lipgloss.NewStyle().
			Foreground(dimColor)


	stepStyle = lipgloss.NewStyle().
			Foreground(primaryColor).
			Bold(true)

	stepDoneStyle = lipgloss.NewStyle().
			Foreground(successColor).
			Bold(true)

	stepCurrentStyle = lipgloss.NewStyle().
			Foreground(highlightColor).
			Bold(true)

	dividerStyle = lipgloss.NewStyle().
			Foreground(dimColor).
			Render
)