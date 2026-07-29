package main

const (
	stateViewing state = iota
	stateSearching
)

type state int

type logLinesMsg []string

type tailTickMsg struct{}

type searchResultMsg struct {
	matches []int
	current int
}

type errMsg struct {
	err error
}