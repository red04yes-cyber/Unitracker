# Security review — 27 novembre 2026

## Perimetro e metodo

Review mirata del codice su `main` (base `98e6ca1ec5ee84759497763a19a823f8f4957560`), dei workflow, dei test di sicurezza e delle PR aperte che propongono hardening. I controlli sono source review e verifica dei workflow, non un penetration test.

La verifica CI sulla base:
- [Build Windows desktop app — run 45902744147](https://github.com/red04yes-cyber/Unitracker/actions/runs/45902744147): completata con successo.
- [Security scan — run 45902744018](https://github.com/red04yes-cyber/Unitracker/actions/runs/45902744018): completata con successo; scansione Gitleaks, pinning Actions e audit NuGet.

## Esiti dei 30 controlli

| # | Area / rischio | Metodo ed evidenza | Esito |
|---|---|---|---|
| 1 | Segreti in repository/history | Gitleaks history + working tree nel workflow Security, run 45902744018 | Superato |
| 2 | Supply chain GitHub Actions | `verify-github-actions-pinning.sh` eseguito dalla CI | Superato |
| 3 | Permessi workflow | `security-scan.yml` e `build-windows.yml`: `contents: read` | Superato |
| 4 | Credenziali checkout persistenti | Entrambi i workflow impostano `persist-credentials: false` | Superato |
| 5 | Vulnerabilità NuGet | Restore con NU1901–NU1904 trattati come errori per app e test | Superato |
| 6 | Token Moodle at-rest | `WindowsTokenVault.cs` + `SecretProtection.cs`: DPAPI per utente, envelope protetto | Superato |
| 7 | Revoca locale token | `PrivacyService.ResetAsync(MoodleConnection)` elimina token e riferimenti; test dedicato | Superato |
| 8 | Credenziali nei backup/import/export | Token e chiavi separati da `StudyData`; import/export non li serializzano | Superato |
| 9 | Validazione endpoint Moodle | `MoodleClient` usa `OutboundEndpointPolicy.ValidateBaseUrl` | Superato |
| 10 | Redirect HTTP | `MoodleClient` usa `AllowAutoRedirect = false` | Superato |
| 11 | Risposta API non limitata | `MaximumApiResponseBytes = 8 MiB` in Moodle client | Superato |
| 12 | Timeout rete | Timeout Moodle 45 secondi e cancellazione propagata | Superato |
| 13 | Download file e token | Download stream limitato a 250 MiB; il trasporto del token sull'endpoint file necessita ancora della remediation proposta | Problema rilevato — [PR #105](https://github.com/red04yes-cyber/Unitracker/pull/105) |
| 14 | Contenimento filesystem e reparse point | Download con `LocalFileSecurity.IsInside`; la scansione privacy non segue root/child link e rimuove solo directory vuote con controlli non ricorsivi | Corretto in questa PR; test aggiunti |
| 15 | Import PDF da file | Limite file 25 MiB e limiti testo/pagina in `PdfServices.cs` | Superato |
| 16 | Adapter PDF da stream | L'adapter di default usa `CopyToAsync` senza bound prima del parser | Problema rilevato — [PR #110](https://github.com/red04yes-cyber/Unitracker/pull/110) |
| 17 | ICS parser input testuale | `ImportText` rifiuta oltre 10 MiB | Superato |
| 18 | ICS file read / TOCTOU | `FileInfo.Length` seguito da `ReadAllTextAsync` non bounded sullo stream | Problema rilevato — [PR #108](https://github.com/red04yes-cyber/Unitracker/pull/108) |
| 19 | SQL injection | Query persistence tramite EF Core; nessuna concatenazione SQL dinamica rilevata nel percorso esaminato | Superato |
| 20 | Import JSON dimensione | `StudyDataImportService.MaximumImportBytes = 64 MiB` | Superato |
| 21 | Import volume record | Limite 100.000 record prima dell'applicazione | Superato |
| 22 | Import deserializzazione/schema | Schema version esplicito, `MaxDepth = 64`, normalizzazione/validazione | Superato |
| 23 | Import Replace | Crea backup di sicurezza prima del salvataggio sostitutivo; senza BackupService rifiuta | Superato |
| 24 | Backup cifratura | Container AES-GCM; chiave DPAPI o derivazione password; legacy dichiarato | Superato |
| 25 | Restore input bounds | Verifica/ripristino legacy e manifest non applicavano uniformemente i limiti prima della lettura/copia | Problema rilevato — [PR #114](https://github.com/red04yes-cyber/Unitracker/pull/114) |
| 26 | ACL database e storage privato | `EnsurePrivateDirectory` ignora il fallimento della restrizione ACL e i writer non falliscono chiusi | Problema rilevato — [PR #106](https://github.com/red04yes-cyber/Unitracker/pull/106) |
| 27 | Reparse point in storage privato | Protezione dei path del database/key directory incompleta sul branch base | Problema rilevato — [PR #106](https://github.com/red04yes-cyber/Unitracker/pull/106) |
| 28 | Formula injection export | CSV neutralizza prefissi formula; XLSX usa stringhe inline, con test regressione | Superato |
| 29 | Privacy export | Note private escluse per default; export/import non includono credenziali | Superato |
| 30 | Diagnostic bundle | Consenso esplicito, log redatti, database/documenti/credenziali esclusi; test | Superato |

## Problema corretto in questa PR

`PrivacyService.CountMoodleFiles` e `DeleteMoodleFiles` enumeravano ricorsivamente il percorso gestito senza rifiutare un root reparse point. La cancellazione dei file controllava poi `IsInside`, ma la pulizia delle directory vuote non applicava lo stesso confine: poteva agire su directory raggiunte tramite link. L'inventario poteva inoltre contare contenuti esterni.

La correzione:
- attraversa directory in modo iterativo e streamma le entry, verificando il root e ogni segmento con `IsInside`;
- ignora file, directory e altri reparse point;
- ri-verifica il file immediatamente prima dello scrub;
- rimuove solo file regolari e directory vuote validate, con `Directory.Delete` non ricorsivo e traversal post-order.

Test aggiunti per root link, child directory link e file link: verificano che l'inventario non conti contenuti esterni e che il reset non modifichi file o directory fuori dal percorso gestito.

## Problemi aperti sul branch base

Le PR #105, #106, #108, #110 e #114 sono ancora aperte e non vengono considerate integrate da questa review. Questa PR non modifica i loro file né le loro remediation. Il conteggio di esiti superati è basato su review/configurazione o CI; non equivale a un penetration test e non certifica il sistema operativo/dispositivo dell'utente.
