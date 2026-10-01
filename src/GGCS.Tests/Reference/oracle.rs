use crate::frame_info::PlayerInput;
use crate::input_queue::InputQueue;
use crate::time_sync::TimeSync;
use crate::{Config, InputStatus, PredictRepeatLast};

struct OracleConfig;

impl Config for OracleConfig {
    type Input = u8;
    type InputPredictor = PredictRepeatLast;
    type State = i32;
    type Address = usize;
}

#[test]
fn ggcs_reference_trace() {
    let mut queue = InputQueue::<OracleConfig>::new();
    let mut previous_output = -1;
    let mut commands = Vec::new();

    for frame in 0..40 {
        let delay = match frame {
            0 => Some(2),
            5 => Some(5),
            10 => Some(1),
            18 => Some(4),
            24 => Some(0),
            _ => None,
        };
        if let Some(delay) = delay {
            queue.set_frame_delay(delay);
            commands.push(format!("{{\"operation\":\"delay\",\"value\":{delay}}}"));
        }

        let input = ((frame * 17 + 3) % 251) as u8;
        let output = queue.add_input(PlayerInput::new(frame, input));
        let mut generated = Vec::new();
        if output >= 0 {
            for output_frame in (previous_output + 1)..=output {
                let value = queue.confirmed_input(output_frame).input;
                generated.push(format!("[{output_frame},{value}]"));
            }
            previous_output = output;
        }
        commands.push(format!(
            "{{\"operation\":\"submit\",\"frame\":{frame},\"input\":{input},\"outputs\":[{}]}}",
            generated.join(",")
        ));
    }

    let mut time = TimeSync::new();
    let mut samples = Vec::new();
    for frame in 0..180 {
        let local = (frame * 7 + 3) % 29 - 14;
        let remote = (frame * 11 + 5) % 31 - 15;
        time.advance_frame(frame, local, remote);
        samples.push(format!(
            "[{frame},{local},{remote},{}]",
            time.average_frame_advantage()
        ));
    }

    println!(
        "GGCS_ORACLE:{{\"delayCommands\":[{}],\"timeSamples\":[{}],\"predictionCommands\":[{}]}}",
        commands.join(","),
        samples.join(","),
        prediction_trace().join(",")
    );
}

fn prediction_trace() -> Vec<String> {
    let commands = [
        ("add", 0, 2),
        ("advance", 0, 0),
        ("advance", 0, 0),
        ("advance", 0, 0),
        ("add", 1, 2),
        ("add", 2, 5),
        ("repair", 0, 0),
        ("advance", 0, 0),
        ("add", 3, 7),
        ("repair", 0, 0),
        ("advance", 0, 0),
        ("add", 4, 7),
        ("repair", 0, 0),
        ("add", 5, 4),
        ("advance", 0, 0),
        ("advance", 0, 0),
        ("advance", 0, 0),
        ("add", 6, 0),
        ("add", 7, 0),
        ("repair", 0, 0),
        ("advance", 0, 0),
        ("add", 8, 0),
        ("repair", 0, 0),
    ];
    let mut queue = InputQueue::<OracleConfig>::new();
    let mut frame = 0;
    let mut state: u64 = 0;
    let mut states = vec![0; 100];
    let mut results = Vec::new();

    for (operation, input_frame, input) in commands {
        let mut advanced = Vec::new();
        match operation {
            "add" => {
                assert_eq!(queue.add_input(PlayerInput::new(input_frame, input)), input_frame);
            }
            "advance" => {
                states[frame as usize] = state;
                let (value, status) = queue.input(frame);
                state = state.wrapping_mul(31).wrapping_add(value as u64);
                advanced.push(format!("[{frame},{value},{}]", status_number(status)));
                frame += 1;
            }
            "repair" => {
                let first = queue.first_incorrect_frame();
                if first >= 0 {
                    state = states[first as usize];
                    queue.reset_prediction();
                    for repaired in first..frame {
                        states[repaired as usize] = state;
                        let (value, status) = queue.input(repaired);
                        state = state.wrapping_mul(31).wrapping_add(value as u64);
                        advanced.push(format!("[{repaired},{value},{}]", status_number(status)));
                    }
                }
            }
            _ => unreachable!(),
        }
        results.push(format!(
            "{{\"operation\":\"{operation}\",\"inputFrame\":{input_frame},\"input\":{input},\"frame\":{frame},\"state\":{state},\"advances\":[{}]}}",
            advanced.join(",")
        ));
    }
    results
}

fn status_number(status: InputStatus) -> u8 {
    match status {
        InputStatus::Confirmed => 0,
        InputStatus::Predicted => 1,
        InputStatus::Disconnected => 2,
    }
}
