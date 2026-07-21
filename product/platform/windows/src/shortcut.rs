#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Shortcut(String);

impl Shortcut {
    pub fn parse(value: impl Into<String>) -> Result<Self, ShortcutError> {
        let value = value.into();
        let normalized = value.replace(' ', "");
        let parts: Vec<&str> = normalized.split('+').collect();
        if parts.len() < 2 || parts.last().is_none_or(|key| key.is_empty()) {
            return Err(ShortcutError::Invalid);
        }
        let mut ctrl = false;
        let mut alt = false;
        let mut shift = false;
        let mut super_key = false;
        for modifier in &parts[..parts.len() - 1] {
            match modifier.to_ascii_lowercase().as_str() {
                "ctrl" | "control" if !ctrl => ctrl = true,
                "alt" if !alt => alt = true,
                "shift" if !shift => shift = true,
                "super" | "command" | "cmd" | "meta" if !super_key => super_key = true,
                _ => return Err(ShortcutError::Invalid),
            }
        }
        if !(ctrl || alt || shift || super_key) {
            return Err(ShortcutError::Invalid);
        }
        let mut canonical = Vec::with_capacity(5);
        if ctrl {
            canonical.push("Ctrl".to_owned());
        }
        if alt {
            canonical.push("Alt".to_owned());
        }
        if shift {
            canonical.push("Shift".to_owned());
        }
        if super_key {
            canonical.push("Super".to_owned());
        }
        canonical.push(
            canonical_key(parts.last().expect("validated key")).ok_or(ShortcutError::Invalid)?,
        );
        Ok(Self(canonical.join("+")))
    }

    #[must_use]
    pub fn as_str(&self) -> &str {
        &self.0
    }
}

fn canonical_key(value: &str) -> Option<String> {
    if value.len() == 1 && value.as_bytes()[0].is_ascii_alphanumeric() {
        return Some(value.to_ascii_uppercase());
    }
    let lowercase = value.to_ascii_lowercase();
    if let Some(function) = lowercase.strip_prefix('f')
        && function
            .parse::<u8>()
            .is_ok_and(|number| (1..=24).contains(&number))
    {
        return Some(lowercase.to_ascii_uppercase());
    }
    match lowercase.as_str() {
        "backspace" => Some("Backspace".into()),
        "delete" => Some("Delete".into()),
        "end" => Some("End".into()),
        "enter" => Some("Enter".into()),
        "escape" | "esc" => Some("Escape".into()),
        "home" => Some("Home".into()),
        "insert" => Some("Insert".into()),
        "pagedown" => Some("PageDown".into()),
        "pageup" => Some("PageUp".into()),
        "space" => Some("Space".into()),
        "tab" => Some("Tab".into()),
        "arrowdown" => Some("ArrowDown".into()),
        "arrowleft" => Some("ArrowLeft".into()),
        "arrowright" => Some("ArrowRight".into()),
        "arrowup" => Some("ArrowUp".into()),
        _ => None,
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ShortcutError {
    Invalid,
    Conflict,
    System,
    RollbackFailed,
}

pub trait ShortcutRegistrar {
    fn register(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError>;
    fn unregister(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError>;
    fn ensure_registered(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError>;
}

pub struct ShortcutManager<R> {
    registrar: R,
    current: Shortcut,
}

impl<R: ShortcutRegistrar> ShortcutManager<R> {
    #[must_use]
    pub fn new(registrar: R, current: Shortcut) -> Self {
        Self { registrar, current }
    }

    #[must_use]
    pub fn current(&self) -> &Shortcut {
        &self.current
    }

    pub fn replace(&mut self, candidate: Shortcut) -> Result<(), ShortcutError> {
        if candidate == self.current {
            return Ok(());
        }
        self.registrar.register(&candidate)?;
        if let Err(error) = self.registrar.unregister(&self.current) {
            return match self.registrar.unregister(&candidate) {
                Ok(()) => Err(error),
                Err(_) => Err(ShortcutError::RollbackFailed),
            };
        }
        self.current = candidate;
        Ok(())
    }

    pub fn restore_after_resume(&mut self) -> Result<(), ShortcutError> {
        self.registrar.ensure_registered(&self.current)
    }

    #[must_use]
    pub fn into_registrar(self) -> R {
        self.registrar
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Default)]
    struct Registrar {
        calls: Vec<String>,
        conflict: Option<String>,
        unregister_fails: bool,
        rollback_fails: bool,
        resume_fails: bool,
    }

    impl ShortcutRegistrar for Registrar {
        fn register(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError> {
            self.calls.push(format!("register:{}", shortcut.as_str()));
            if self.conflict.as_deref() == Some(shortcut.as_str()) {
                Err(ShortcutError::Conflict)
            } else {
                Ok(())
            }
        }

        fn unregister(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError> {
            self.calls.push(format!("unregister:{}", shortcut.as_str()));
            if self.unregister_fails {
                self.unregister_fails = false;
                Err(ShortcutError::System)
            } else if self.rollback_fails {
                Err(ShortcutError::System)
            } else {
                Ok(())
            }
        }

        fn ensure_registered(&mut self, shortcut: &Shortcut) -> Result<(), ShortcutError> {
            self.calls.push(format!("ensure:{}", shortcut.as_str()));
            if self.resume_fails {
                Err(ShortcutError::System)
            } else {
                Ok(())
            }
        }
    }

    fn shortcut(value: &str) -> Shortcut {
        Shortcut::parse(value).unwrap()
    }

    #[test]
    fn requires_a_modifier_and_keeps_same_value_idempotent() {
        assert_eq!(Shortcut::parse("A"), Err(ShortcutError::Invalid));
        assert_eq!(Shortcut::parse("Alt+A+B"), Err(ShortcutError::Invalid));
        assert_eq!(Shortcut::parse("Ctrl+Ctrl"), Err(ShortcutError::Invalid));
        assert_eq!(Shortcut::parse("Ctrl+Mystery"), Err(ShortcutError::Invalid));
        assert_eq!(
            Shortcut::parse("Ctrl+Control+A"),
            Err(ShortcutError::Invalid)
        );
        let mut manager = ShortcutManager::new(Registrar::default(), shortcut("Alt+Shift+A"));

        manager.replace(shortcut("shift + alt + a")).unwrap();

        assert!(manager.into_registrar().calls.is_empty());
    }

    #[test]
    fn registers_candidate_before_removing_the_working_shortcut() {
        let mut manager = ShortcutManager::new(Registrar::default(), shortcut("Alt+Shift+A"));

        manager.replace(shortcut("Ctrl+Shift+S")).unwrap();

        assert_eq!(manager.current().as_str(), "Ctrl+Shift+S");
        assert_eq!(
            manager.into_registrar().calls,
            ["register:Ctrl+Shift+S", "unregister:Alt+Shift+A"]
        );
    }

    #[test]
    fn conflict_or_rollback_failure_preserves_the_previous_setting() {
        let registrar = Registrar {
            conflict: Some("Ctrl+Shift+S".into()),
            ..Registrar::default()
        };
        let mut manager = ShortcutManager::new(registrar, shortcut("Alt+Shift+A"));
        assert_eq!(
            manager.replace(shortcut("Ctrl+Shift+S")),
            Err(ShortcutError::Conflict)
        );
        assert_eq!(manager.current().as_str(), "Alt+Shift+A");

        let registrar = Registrar {
            unregister_fails: true,
            ..Registrar::default()
        };
        let mut manager = ShortcutManager::new(registrar, shortcut("Alt+Shift+A"));
        assert_eq!(
            manager.replace(shortcut("Ctrl+Shift+S")),
            Err(ShortcutError::System)
        );
        assert_eq!(manager.current().as_str(), "Alt+Shift+A");
        assert_eq!(
            manager.into_registrar().calls,
            [
                "register:Ctrl+Shift+S",
                "unregister:Alt+Shift+A",
                "unregister:Ctrl+Shift+S"
            ]
        );

        let registrar = Registrar {
            unregister_fails: true,
            rollback_fails: true,
            ..Registrar::default()
        };
        let mut manager = ShortcutManager::new(registrar, shortcut("Alt+Shift+A"));
        assert_eq!(
            manager.replace(shortcut("Ctrl+Shift+S")),
            Err(ShortcutError::RollbackFailed)
        );
        assert_eq!(manager.current().as_str(), "Alt+Shift+A");
    }

    #[test]
    fn resume_failure_keeps_the_expected_shortcut() {
        let registrar = Registrar {
            resume_fails: true,
            ..Registrar::default()
        };
        let mut manager = ShortcutManager::new(registrar, shortcut("Alt+Shift+A"));

        assert_eq!(manager.restore_after_resume(), Err(ShortcutError::System));
        assert_eq!(manager.current().as_str(), "Alt+Shift+A");
    }
}
