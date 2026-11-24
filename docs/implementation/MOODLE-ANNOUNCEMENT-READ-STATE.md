# Stato di lettura degli aggiornamenti Moodle

## Contratto

Le voci `moodle-discussion:{id}` rappresentano discussioni Moodle nella inbox:
- un elemento nuovo è non letto;
- una sincronizzazione identica aggiorna il timestamp di acquisizione ma conserva lo stato letto/non letto;
- una modifica a corso, titolo, testo o destinazione riporta l'elemento a non letto;
- l'ID locale e la chiave sorgente restano stabili, quindi la sincronizzazione non crea duplicati.

`AnnouncementSyncService.MarkRead` e `MarkAllRead` modificano lo stato in memoria. Il chiamante persiste poi lo snapshot tramite `IStudyDataStore`. Il pulsante esistente "Segna tutti come letti" del centro notifiche salva lo stato prima di chiudere gli avvisi temporanei; se il salvataggio fallisce, ripristina i flag e mantiene la inbox visibile.

## Tipi che non sono messaggi della inbox

Le righe `moodle-activity:{courseId}:{moduleId}` usano ancora `Announcement.IsNew` per rappresentare lo stato di completamento dell'attività (non completata / completata). Sono escluse dalle azioni di lettura per evitare di alterare il progresso Moodle.

Le righe `moodle-activity-api:{courseId}:{key}` sono metadati tecnici delle attività e non vengono mostrate come messaggi della inbox. L'upsert condiviso evita comunque che una sincronizzazione identica ricrei uno stato "nuovo" artificiale.

## Normalizzazione e privacy

- Chiave sorgente limitata a 256 caratteri e rifiuto di caratteri di controllo.
- Corso, titolo e riepilogo hanno limiti rispettivamente di 200, 300 e 1.800 caratteri.
- I riepiloghi restano testo semplice; i caratteri di controllo non necessari vengono rimossi.
- I link accettano solo URI assoluti HTTP/HTTPS, senza user-info, frammenti o parametri di autenticazione/token noti.
- I dettagli delle API attività non riutilizzano l'URL del syllabus del corso: non esiste un deep link affidabile per ogni dettaglio, e l'URL del syllabus può contenere parametri privati.

La UI può usare `StudyWorkspaceViewModel.Notifications` per la lista unificata e `ClearNotificationsCommand` per la lettura di massa. Per un'azione singola, usare `AnnouncementSyncService.MarkRead(data, announcementId)` e salvare lo snapshot. Nessuna modifica XAML o migrazione database è richiesta: `Announcement.IsNew` è già persistito dal mapper SQLite.
