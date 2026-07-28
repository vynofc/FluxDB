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

func formatBytes(bytes int64) string {
	const unit = 1024
	if bytes < unit {
		return fmt.Sprintf("%d B", bytes)
	}
	div, exp := int64(unit), 0
	for n := bytes / unit; n >= unit; n /= unit {
		div *= unit
		exp++
	}
	return fmt.Sprintf("%.1f %cB", float64(bytes)/float64(div), "KMGTPE"[exp])
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