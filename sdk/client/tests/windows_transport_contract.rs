#![cfg(windows)]

use std::thread;
use std::time::Duration;

use snaploom_capture_client::local_transport::{
    EndpointPaths, LeaderOutcome, TransportSecurityError, UnixLeader, connect_authenticated,
};

#[test]
fn windows_pipe_elects_one_leader_rejects_remote_and_authenticates_peer_tokens() {
    const CHILD: &str = "SNAPLOOM_WINDOWS_LEADER_CHILD";
    let paths = EndpointPaths::for_current_session(1).unwrap();
    if std::env::var_os(CHILD).is_some() {
        match UnixLeader::try_bind(&paths).unwrap() {
            LeaderOutcome::Leader(_leader) => {
                println!("SNAPLOOM_ROLE_LEADER");
                thread::sleep(Duration::from_millis(750));
            }
            LeaderOutcome::Existing => println!("SNAPLOOM_ROLE_EXISTING"),
        }
        return;
    }

    let executable = std::env::current_exe().unwrap();
    let outputs = (0..20)
        .map(|_| {
            std::process::Command::new(&executable)
                .args([
                    "--exact",
                    "windows_pipe_elects_one_leader_rejects_remote_and_authenticates_peer_tokens",
                    "--nocapture",
                ])
                .env(CHILD, "1")
                .stdout(std::process::Stdio::piped())
                .spawn()
                .unwrap()
        })
        .collect::<Vec<_>>()
        .into_iter()
        .map(|child| child.wait_with_output().unwrap())
        .collect::<Vec<_>>();
    assert!(outputs.iter().all(|output| output.status.success()));
    assert_eq!(
        outputs
            .iter()
            .filter(|output| {
                String::from_utf8_lossy(&output.stdout).contains("SNAPLOOM_ROLE_LEADER")
            })
            .count(),
        1
    );

    let LeaderOutcome::Leader(mut leader) = UnixLeader::try_bind(&paths).unwrap() else {
        panic!("leader must be available after all child processes exit");
    };
    for _ in 0..16 {
        let client_paths = paths.clone();
        let client = thread::spawn(move || connect_authenticated(&client_paths).unwrap());
        let server_stream = loop {
            match leader.accept_authenticated() {
                Ok(stream) => break stream,
                Err(TransportSecurityError::Io(std::io::ErrorKind::WouldBlock)) => {
                    thread::sleep(Duration::from_millis(1));
                }
                Err(error) => panic!("pipe accept failed: {error:?}"),
            }
        };
        let client_stream = client.join().unwrap();
        drop((server_stream, client_stream));
    }
    drop(leader);
}
