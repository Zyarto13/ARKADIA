# Release signing

The GitHub `Publish release` workflow signs the Velopack application payload and setup, and the Inno Setup compatibility installer, with Authenticode when both of these repository secrets are configured:

- `ARKADIA_SIGNING_CERTIFICATE_BASE64`: the base64-encoded PFX file containing the code-signing certificate and private key.
- `ARKADIA_SIGNING_CERTIFICATE_PASSWORD`: the PFX password.

Use a certificate issued for code signing by a trusted certificate authority, or apply to the SignPath Foundation program for eligible open-source projects. SignPath approval is not guaranteed. Do not commit the PFX, password, or private key to the repository.

To configure a PFX certificate, encode the file locally without adding line breaks, then add the resulting value and certificate password under the repository's **Settings → Secrets and variables → Actions → New repository secret**. Restrict repository access to trusted maintainers because anyone able to modify this workflow and trigger a release could attempt to use the signing secret.

When configured, the workflow imports the certificate into the temporary GitHub runner, signs the Velopack application files and setup using SHA-256 with a timestamp, signs the Inno Setup compatibility installer, verifies both setup signatures, removes the temporary certificate, and then generates the SHA-256 checksums. If either secret is missing, the workflow explicitly reports that the release is unsigned and continues publishing.

Until a trusted signing certificate is configured, Windows SmartScreen may show a publisher-reputation warning for downloaded installers. A SHA-256 checksum detects accidental or malicious file changes but does not authenticate the publisher. A SmartScreen warning is not the same as a Microsoft Defender malware detection.
