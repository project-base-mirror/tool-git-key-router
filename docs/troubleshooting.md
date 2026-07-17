# Troubleshooting

## Inspect raw command results

Every Git, SSH, and ssh-keygen action exposes the executable path, arguments, stdout, stderr, exit code, timeout state, cancellation state, and duration.

## Multiple executable candidates

Diagnostics lists every existing candidate found in PATH and common Windows locations. The first candidate in the documented lookup order is selected; no PATH or installation setting is changed.

## GitHub SSH returns exit code 1 after success

This is normal for `ssh -T git@github.com` style tests. GitKeyRouter additionally checks for the GitHub authentication-success text.

## Unmanaged SSH Host conflict

If a manually written `Host` uses the same alias as a GitKeyRouter identity, diagnostics reports a warning. The application does not delete or rewrite the manual entry.

## Duplicate managed block

GitKeyRouter stops updating that alias until the duplicate marker blocks are reviewed. This avoids deleting an arbitrary block.
