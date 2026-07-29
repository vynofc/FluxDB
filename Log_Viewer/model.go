package main

type state int

const (
	stateViewing state = iota
)

type logLinesMsg []string

type tailTickMsg struct{}

type errMsg struct{ error }