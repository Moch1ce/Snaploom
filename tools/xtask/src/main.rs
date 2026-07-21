use std::process::{Command, ExitCode};

fn run(program: &str, args: &[&str]) -> bool {
    println!("+ {program} {}", args.join(" "));
    Command::new(program)
        .args(args)
        .status()
        .is_ok_and(|status| status.success())
}

fn main() -> ExitCode {
    let command = std::env::args().nth(1).unwrap_or_else(|| "check".into());
    let ok = match command.as_str() {
        "check" => {
            run(
                "cargo",
                &["test", "--manifest-path", "sdk/Cargo.toml", "--locked"],
            ) && run(
                "cargo",
                &["test", "--manifest-path", "product/Cargo.toml", "--locked"],
            ) && run("pnpm", &["run", "check"])
        }
        "boundaries" => run("node", &["tools/architecture/check-boundaries.mjs"]),
        "goldens-update" => run(
            "pnpm",
            &[
                "--filter",
                "@snaploom/overlay-editor",
                "run",
                "goldens:update",
            ],
        ),
        other => {
            eprintln!("unknown xtask command: {other}");
            false
        }
    };

    if ok {
        ExitCode::SUCCESS
    } else {
        ExitCode::FAILURE
    }
}
