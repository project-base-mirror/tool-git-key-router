# Troubleshooting

## Inspect raw command results

Every Git, SSH, and ssh-keygen action exposes the executable path, arguments, stdout, stderr, exit code, timeout state, cancellation state, and duration.

## Multiple executable candidates

Diagnostics lists every existing candidate found in PATH and common Windows locations. The first candidate in the documented lookup order is selected; no PATH or installation setting is changed.

## Git service SSH returns a non-zero exit code after success

This is normal for some `ssh -T git@host` style tests. GitKeyRouter uses the selected GitHub, GitLab, Gitea, or generic provider adapter to classify the service-specific authentication response.

## A Git service reports `Key is invalid`

Git hosting SSH key forms expect a valid OpenSSH public-key line. RFC4716/SSH2 blocks, PEM/PKCS8 blocks, private keys, malformed Base64, and mismatched key algorithm blobs are not copied by the `Copy public key` action.

In the identities list, select the detected key variant and choose `Convert format` → `OpenSSH public key`. GitKeyRouter writes a separate `*.openssh.pub` file and preserves the source file. Existing target files are only replaced after explicit confirmation and are backed up first.

PuTTY PPK files are detected but require PuTTYgen; GitKeyRouter does not attempt to parse or display private-key contents.

## Unmanaged SSH Host conflict

If a manually written `Host` uses the same alias as a GitKeyRouter identity, diagnostics reports a warning. The application does not delete or rewrite the manual entry.

## Duplicate managed block

GitKeyRouter stops updating that alias until the duplicate marker blocks are reviewed. This avoids deleting an arbitrary block.

## Restoring a backup reports an unsupported schema

GitKeyRouter validates the application configuration before restoring it. A backup created by a newer release is rejected when its schema is higher than the current application supports; upgrade GitKeyRouter before restoring that snapshot.
