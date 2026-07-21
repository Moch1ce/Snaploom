pub const PROTOCOL_MAJOR: u16 = 1;
pub const PROTOCOL_MINOR: u16 = 0;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn first_public_protocol_major_is_one() {
        assert_eq!(PROTOCOL_MAJOR, 1);
    }
}
