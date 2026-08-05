package main

import (
	"fmt"
	"strings"
)

func formatLogs(logs []string) string {
	if len(logs) == 0 {
		return ""
	}
	var sb strings.Builder
	for _, line := range logs {
		sb.WriteString(logStyle.Render("  " + line))
		sb.WriteString("\n")
	}
	return sb.String()
}

func formatStepHeader(step, total int, title string, done bool, current bool) string {
	prefix := fmt.Sprintf("[%d/%d]", step, total)
	if done {
		return stepDoneStyle.Render(prefix+" ✓ ") + title
	}
	if current {
		return stepCurrentStyle.Render(prefix+" ▶ ") + title
	}
	return stepStyle.Render(prefix+"   ") + dimStyle.Render(title)
}