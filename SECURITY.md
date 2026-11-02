# Security

Please report a security problem privately, through
[GitHub's private reporting](https://github.com/volodya88i/jGlancer-Windows/security/advisories/new), not in a public
issue. Include the version and the steps to reproduce it.

## How updates are protected

- The app asks only `api.github.com` for the latest version number. The download address is built from a fixed
  repository and that number, which must look like `1.2.3`; it is never taken from the answer.
- Every release has `jGlancer-Setup-<version>.exe.sig`: an ECDSA P-256 signature over the installer's SHA-256 and the
  version. The public key is built into the app; the private key is never in this repository.
- The app starts an installer only if the signature matches, so a replaced file, or an old signed installer offered
  under a new number, is refused and deleted.
