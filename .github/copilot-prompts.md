# Copilot Prompts

Usage guidelines for AI collaboration on VisionShell.

## Language
Use Chinese to communicate during session.

## Context Setting
When starting a session, remind the agent:
> "We are working on VisionShell, a modular WPF app for vision control. Plugins are isolated, ILogger is injected, and exceptions must be logged, not thrown."

## Task Specifics

### Creating a New Plugin
> "Create a new Stage plugin named 'NewStage'. Scaffold the project in src/plugins/devices/VisionShell.Plugins.NewStage, set OutputPath to 'plugins/new-stage', and implement the IStage interface with dummy moves."

### Adding a Feature
> "Add a new feature module 'DataAnalysis'. It should be a project reference in App, have a View/ViewModel, and register a route 'analysis/main' in the Main region."
