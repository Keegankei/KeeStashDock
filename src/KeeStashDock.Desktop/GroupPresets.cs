using KeeStashDock.Core.Services;

namespace KeeStashDock.Desktop;

/// <summary>
/// Built-in one-click group templates. A preset is an ordinary
/// in-memory GroupPackage fed through the same import pipeline as .ksdgrp
/// files, so conflict handling (overwrite / rename) is identical.
/// Titles use terse developer English on purpose: commands are read in
/// English by convention and the content must stay copy-paste exact.
/// </summary>
public static class GroupPresets
{
    /// <summary>(display-locale-key, package) pairs shown in the preset menu.</summary>
    public static IReadOnlyList<(string NameKey, Func<GroupIoService.GroupPackage> Build)>
        All { get; } = new List<(string, Func<GroupIoService.GroupPackage>)>
    {
        ("Workbench.Category.Preset.Git",        BuildGit),
        ("Workbench.Category.Preset.Docker",     BuildDocker),
        ("Workbench.Category.Preset.Shell",      BuildShell),
        ("Workbench.Category.Preset.PowerShell", BuildPowerShell),
        ("Workbench.Category.Preset.Npm",        BuildNpm),
        ("Workbench.Category.Preset.Python",     BuildPython),
        ("Workbench.Category.Preset.Sql",        BuildSql),
    };

    private static GroupIoService.GroupPackage BuildGit() => new(1, "Git", new()
    {
        new("Init repository",        "git init"),
        new("Clone repository",       "git clone <url>"),
        new("Status",                 "git status"),
        new("Stage all changes",      "git add -A"),
        new("Commit",                 "git commit -m \"<message>\""),
        new("Amend last commit",      "git commit --amend --no-edit"),
        new("Push (set upstream)",    "git push -u origin <branch>"),
        new("Pull with rebase",       "git pull --rebase"),
        new("New branch + switch",    "git switch -c <branch>"),
        new("List branches",          "git branch -a"),
        new("Merge branch",           "git merge <branch>"),
        new("Compact log graph",      "git log --oneline --graph --decorate -20"),
        new("Diff staged",            "git diff --staged"),
        new("Stash",                  "git stash"),
        new("Stash pop",              "git stash pop"),
        new("Undo last commit (keep changes)", "git reset --soft HEAD~1"),
        new("Discard local changes",  "git checkout -- <file>"),
        new("Remotes",                "git remote -v"),
        new("Tag annotated",          "git tag -a v1.0.0 -m \"<message>\""),
    });

    private static GroupIoService.GroupPackage BuildDocker() => new(1, "Docker", new()
    {
        new("List running containers",   "docker ps"),
        new("List all containers",       "docker ps -a"),
        new("List images",               "docker images"),
        new("Run (detached, port map)",  "docker run -d -p <host>:<container> --name <name> <image>"),
        new("Interactive shell",         "docker exec -it <container> /bin/bash"),
        new("Follow logs",               "docker logs -f --tail 100 <container>"),
        new("Stop / remove container",   "docker stop <container> && docker rm <container>"),
        new("Build image",               "docker build -t <name>:<tag> ."),
        new("Copy file out",             "docker cp <container>:<path> <local-path>"),
        new("Compose up",                "docker compose up -d"),
        new("Compose down",              "docker compose down"),
        new("Compose rebuild",           "docker compose up -d --build"),
        new("Prune unused",              "docker system prune -f"),
        new("Inspect container",         "docker inspect <container>"),
        new("Container stats",           "docker stats --no-stream"),
    });

    private static GroupIoService.GroupPackage BuildShell() => new(1, "Linux Shell", new()
    {
        new("Find file by name",         "find . -name \"<pattern>\" -type f"),
        new("Search text in files",      "grep -rn \"<text>\" ."),
        new("Disk usage (top level)",    "du -h --max-depth=1 | sort -hr"),
        new("Free disk space",           "df -h"),
        new("Follow log file",           "tail -f <file>"),
        new("Process by name",           "ps aux | grep <name>"),
        new("Kill by port",              "kill -9 $(lsof -t -i:<port>)"),
        new("Listening ports",           "ss -tulpn"),
        new("Extract tar.gz",            "tar -xzvf <file>.tar.gz"),
        new("Create tar.gz",             "tar -czvf <name>.tar.gz <dir>"),
        new("Copy over SSH",             "scp <file> user@host:/path/"),
        new("Sync directories",          "rsync -avz --progress <src>/ <dst>/"),
        new("Make executable",           "chmod +x <file>"),
        new("Change owner (recursive)",  "chown -R user:group <dir>"),
        new("History search",            "history | grep <cmd>"),
    });

    private static GroupIoService.GroupPackage BuildPowerShell() => new(1, "PowerShell", new()
    {
        new("List processes by CPU",     "Get-Process | Sort-Object CPU -Descending | Select-Object -First 10"),
        new("Kill process by name",      "Stop-Process -Name <name> -Force"),
        new("Find file recursively",     "Get-ChildItem -Recurse -Filter <pattern>"),
        new("Search text in files",      "Select-String -Path .\\*.<ext> -Pattern \"<text>\""),
        new("Who owns this port",        "Get-NetTCPConnection -LocalPort <port> | Select-Object OwningProcess"),
        new("Test connection",           "Test-NetConnection <host> -Port <port>"),
        new("Download file",             "Invoke-WebRequest -Uri <url> -OutFile <file>"),
        new("Unzip archive",             "Expand-Archive <file>.zip -DestinationPath <dir>"),
        new("Zip folder",                "Compress-Archive -Path <dir> -DestinationPath <name>.zip"),
        new("Environment variables",     "Get-ChildItem Env:"),
        new("Execution policy (user)",   "Set-ExecutionPolicy -Scope CurrentUser RemoteSigned"),
        new("Services (running)",        "Get-Service | Where-Object Status -eq Running"),
        new("Restart service",           "Restart-Service -Name <service>"),
        new("Measure command time",      "Measure-Command { <command> }"),
    });

    private static GroupIoService.GroupPackage BuildNpm() => new(1, "npm / Node.js", new()
    {
        new("Init project",              "npm init -y"),
        new("Install dependencies",      "npm install"),
        new("Add package",               "npm install <package>"),
        new("Add dev package",           "npm install -D <package>"),
        new("Remove package",            "npm uninstall <package>"),
        new("Run script",                "npm run <script>"),
        new("Outdated packages",         "npm outdated"),
        new("Update packages",           "npm update"),
        new("Clean install (CI)",        "npm ci"),
        new("List globals",              "npm list -g --depth=0"),
        new("Clear cache",               "npm cache clean --force"),
        new("Registry mirror",           "npm config set registry https://registry.npmmirror.com"),
        new("Run without installing",    "npx <package>"),
        new("Node REPL with await",      "node --experimental-repl-await"),
    });

    private static GroupIoService.GroupPackage BuildPython() => new(1, "Python / pip", new()
    {
        new("Create virtual env",        "python -m venv .venv"),
        new("Activate venv (Windows)",   ".venv\\Scripts\\activate"),
        new("Activate venv (bash)",      "source .venv/bin/activate"),
        new("Install package",           "pip install <package>"),
        new("Install from requirements", "pip install -r requirements.txt"),
        new("Freeze requirements",       "pip freeze > requirements.txt"),
        new("Upgrade pip",               "python -m pip install --upgrade pip"),
        new("Uninstall package",         "pip uninstall <package>"),
        new("Show package info",         "pip show <package>"),
        new("Run module",                "python -m <module>"),
        new("Quick HTTP server",         "python -m http.server 8000"),
        new("Pretty-print JSON",         "python -m json.tool <file>.json"),
        new("Run tests",                 "python -m pytest -v"),
        new("Profile a script",          "python -m cProfile -s cumulative <script>.py"),
    });

    private static GroupIoService.GroupPackage BuildSql() => new(1, "SQL", new()
    {
        new("Select with filter",        "SELECT * FROM <table> WHERE <condition> LIMIT 100;"),
        new("Count by group",            "SELECT <col>, COUNT(*) FROM <table> GROUP BY <col> ORDER BY COUNT(*) DESC;"),
        new("Insert row",                "INSERT INTO <table> (<cols>) VALUES (<values>);"),
        new("Update with filter",        "UPDATE <table> SET <col> = <value> WHERE <condition>;"),
        new("Delete with filter",        "DELETE FROM <table> WHERE <condition>;"),
        new("Join two tables",           "SELECT a.*, b.<col> FROM <a> a JOIN <b> b ON a.id = b.a_id;"),
        new("Create table",              "CREATE TABLE <table> (id INTEGER PRIMARY KEY, name TEXT NOT NULL);"),
        new("Add column",                "ALTER TABLE <table> ADD COLUMN <col> <type>;"),
        new("Create index",              "CREATE INDEX idx_<name> ON <table>(<col>);"),
        new("Duplicate rows",            "SELECT <col>, COUNT(*) FROM <table> GROUP BY <col> HAVING COUNT(*) > 1;"),
        new("Top-N per group (window)",  "SELECT * FROM (SELECT t.*, ROW_NUMBER() OVER (PARTITION BY <grp> ORDER BY <ord> DESC) rn FROM <table> t) WHERE rn <= 3;"),
        new("Explain query plan",        "EXPLAIN QUERY PLAN SELECT * FROM <table> WHERE <condition>;"),
    });
}
