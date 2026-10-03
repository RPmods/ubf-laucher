# UBFLauncherUpdater

Self-contained Windows helper invoked by UBFLauncher. It waits for the launcher process, validates archive paths, stages the ZIP payload, replaces files with rollback on failure, and starts the updated launcher.

The launcher validates the ZIP SHA-256 against the remote `version.json` before starting this helper. Do not publish unsigned metadata or an archive from an untrusted source.
