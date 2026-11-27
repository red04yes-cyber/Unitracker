# Limiti dello stream PDF

`IPdfTextExtractor` mantiene un adattatore predefinito per implementazioni che espongono soltanto `ExtractAsync(string)`. L'adattatore scrive lo stream in un file temporaneo privato prima di delegare l'estrazione.

Il trasferimento applica ora `PdfImportService.MaximumFileBytes` durante la copia, non soltanto dopo la creazione del file. Legge al massimo un byte oltre il limite per rilevare l'eccedenza, rifiuta gli stream vuoti, rispetta la cancellazione e rimuove il file temporaneo anche in caso di errore.

Il test usa uno stream non seekable generato e verifica che l'adattatore predefinito si fermi esattamente a `MaximumFileBytes + 1`, senza delegare un payload sovradimensionato all'estrattore.

Il limite resta 25 MiB e non sono state aggiunte dipendenze.
