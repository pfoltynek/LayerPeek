# Manuale utente di LayerPeek

*Per LayerPeek 0.5 su Windows 10/11 e macOS (Apple Silicon).* · [Altre lingue](../README.md)

LayerPeek tiene d’occhio le tue stampanti 3D Prusa attraverso la rete locale e ne mostra lo stato nell’area di notifica
di Windows o nella barra dei menu di macOS. Non ha una finestra principale: tutto parte dal menu dell’icona. Questo
manuale descrive entrambi i sistemi; dove Windows e macOS si comportano in modo diverso, il testo lo dice o mostra
entrambe le varianti.

> [!NOTE]
> LayerPeek è un progetto indipendente della community, non un prodotto ufficiale di Prusa Research.

## Indice

1. [Cosa ti serve](#1-cosa-ti-serve)
2. [Installazione](#2-installazione)
3. [Primi passi](#3-primi-passi)
4. [L’icona e il suo menu](#4-licona-e-il-suo-menu)
5. [Finestra della telecamera](#5-finestra-della-telecamera)
6. [Widget](#6-widget)
7. [Notifiche](#7-notifiche)
8. [Registrazione, dashcam e timelapse](#8-registrazione-dashcam-e-timelapse)
9. [Rilevamento degli errori di stampa (sperimentale)](#9-rilevamento-degli-errori-di-stampa-sperimentale)
10. [Diagnostica](#10-diagnostica)
11. [Guida alle impostazioni](#11-guida-alle-impostazioni)
12. [Aggiornamenti](#12-aggiornamenti)
13. [Dove vengono salvati i tuoi dati](#13-dove-vengono-salvati-i-tuoi-dati)
14. [Accesso sicuro a PrusaLink](#14-accesso-sicuro-a-prusalink)
15. [Risoluzione dei problemi](#15-risoluzione-dei-problemi)
16. [Disinstallazione](#16-disinstallazione)

## 1. Cosa ti serve

- **Una stampante Prusa con firmware Buddy 6.x e PrusaLink**: CORE One (testata), MK4/MK4S, MK3.9/MK3.9S, MK3.5/MK3.5S,
  XL e MINI/MINI+. La Original Prusa MK3/MK3S non ha PrusaLink integrato e non è supportata.
- **Un computer sulla stessa rete locale** della stampante:
  - Windows 10 o 11, x64 (Windows su ARM non è supportato),
  - macOS su Apple Silicon (M1 o successivo).
- **Una telecamera (facoltativa):** qualsiasi telecamera con uno stream **RTSP H.264** raggiungibile dal tuo computer,
  per esempio la telecamera Prusa Buddy3D. Anche senza telecamera hai comunque l’icona, le notifiche e la diagnostica.

LayerPeek funziona solo nella tua rete locale. Non usa Prusa Connect né altri servizi cloud. Quando sei fuori casa puoi
raggiungere le stampanti tramite una VPN verso la tua rete domestica.

Finora è stata testata solo la CORE One. Hai un altro modello? Invia un breve [rapporto di compatibilità della stampante](https://github.com/pfoltynek/LayerPeek/issues/new?template=printer_compatibility.yml) (modulo in inglese): basta una prova su Windows o macOS.

## 2. Installazione

Scarica l’ultima versione dalla pagina [Releases](https://github.com/pfoltynek/LayerPeek/releases).

> [!IMPORTANT]
> Gli aggiornamenti all’interno delle versioni 1.x mantengono le tue impostazioni; i file di impostazioni delle
> versioni precedenti vengono sempre caricati. Le build non sono firmate da Microsoft né autenticate (notarized) da
> Apple. Per questo il sistema ti avvisa al primo avvio; i passaggi qui sotto spiegano come proseguire.

### 2.1 Windows

Puoi scegliere tra due pacchetti:

| Pacchetto | Cosa fa |
|---|---|
| **Programma di installazione** `LayerPeek-X.Y.Z-win-x64-Setup.exe` (consigliato) | Installa LayerPeek solo per il tuo utente, senza diritti di amministratore, in `%LOCALAPPDATA%\LayerPeek`, lo aggiunge al menu Start, lo avvia dopo l’accesso e può aggiornarsi da solo. |
| **ZIP** `LayerPeek-X.Y.Z-win-x64.zip` | Estrailo dove vuoi ed esegui `LayerPeek.exe`. Non si avvia dopo l’accesso e non si aggiorna da solo (apre invece la pagina della release). |

Al primo avvio Windows SmartScreen mostra **«Windows ha protetto il PC»** con *Autore sconosciuto*. Fai clic su
**Ulteriori informazioni** e poi su **Esegui comunque**.

### 2.2 macOS

| Pacchetto | Cosa fa |
|---|---|
| **Immagine disco** `LayerPeek-X.Y.Z-osx-arm64.dmg` (consigliata) | Aprila e trascina **LayerPeek** in **Applicazioni**. |
| **Archivio** `LayerPeek-X.Y.Z-osx-arm64.tar.gz` | Contiene `LayerPeek.app`; estrailo e sposta l’app in **Applicazioni**. |

Avvia sempre LayerPeek da **Applicazioni**, non direttamente dall’immagine disco o dalla cartella Download: solo da lì
può aprirsi al login e aggiornarsi da solo.

Al primo avvio Gatekeeper blocca l’app (*«Apple non ha potuto verificare…»*, in inglese *"Apple could not verify…"*):

1. Fai clic su **Fine**.
2. Apri **Impostazioni di Sistema → Privacy e sicurezza**, scorri verso il basso e fai clic su **Apri comunque** accanto
   al messaggio su LayerPeek.
3. Conferma con la password o con Touch ID e apri di nuovo LayerPeek.

Gli utenti esperti possono invece rimuovere l’attributo di quarantena nel Terminale:
`xattr -dr com.apple.quarantine /Applications/LayerPeek.app`.

LayerPeek vive solo nella barra dei menu. Non ha un’icona nel Dock e non compare nel selettore delle app (⌘⇥).

## 3. Primi passi

### 3.1 Attivare PrusaLink sulla stampante

Sul display della stampante apri **Impostazioni → Rete → PrusaLink** (Settings → Network → PrusaLink). Verifica che
PrusaLink sia attivo e annota:

- l’**indirizzo IP** della stampante (o il suo nome host),
- il **nome utente** (quello predefinito è `maker`),
- la **password**.

### 3.2 Aggiungere la stampante

1. Trova l’icona di LayerPeek:
   - **Windows:** nell’area di notifica, all’estremità destra della barra delle applicazioni. Se non la vedi, fai clic
     sulla freccia **`^`** (Mostra icone nascoste) e trascina l’icona sulla barra delle applicazioni, così resta sempre
     visibile.
   - **macOS:** nella barra dei menu in alto sullo schermo.
2. Apri il menu dell’icona (**Windows:** clic destro sull’icona, **macOS:** clic sull’icona) e scegli
   **Aggiungi stampante…**. Si apre la finestra **Impostazioni** sulla scheda **Stampanti**.
3. Compila:
   - **Nome:** un nome a piacere, ad es. `CORE One`.
   - **Indirizzo:** un indirizzo IP o un nome host, ad es. `192.168.1.20` oppure `http://prusa-mk4.local`.
   - **Nome utente** e **Password:** i valori presi dalla stampante.
4. Fai clic su **Verifica connessione**. Se la connessione funziona, vengono mostrati il codice del modello e la
   versione del firmware.
5. Fai clic su **Salva**.

<p align="center">
  <img src="../../docs/images/it/settings-printers.png" alt="Impostazioni, scheda Stampanti con tre stampanti" width="620">
</p>

L’icona inizia subito a mostrare lo stato della stampante. Aggiungi altre stampanti allo stesso modo: **Aggiungi
stampante** nella scheda **Stampanti**.

**macOS 15 e successivi:** alla prima connessione a una stampante o a una telecamera il sistema chiede l’accesso alla
**Rete locale**. Fai clic su **Consenti**. Se l’hai negato, vedi [Risoluzione dei problemi](#15-risoluzione-dei-problemi).

La password viene salvata in **Gestione credenziali** di Windows o nel **Portachiavi** di macOS, mai nel file delle
impostazioni. Quando in seguito modifichi una stampante, il campo della password mostra **Invariata**; lascialo vuoto
per mantenere la password salvata.

**Avviso sotto l'indirizzo:** un indirizzo `http://` che può trovarsi fuori dalla rete locale (un indirizzo IP pubblico,
o un nome host diverso da un nome senza punto o che termina con `.local`, `.lan` o `.home.arpa`) mostra un avviso al
posto del suggerimento. PrusaLink accede senza cifratura, quindi la password potrebbe essere intercettata lungo il
percorso. Il salvataggio resta possibile. Meglio usare `https://` tramite un reverse proxy, o una VPN. **Maggiori
informazioni** accanto all'avviso apre il capitolo [Accesso sicuro a PrusaLink](#14-accesso-sicuro-a-prusalink).

### 3.3 Aggiungere la telecamera (facoltativo)

1. In **Impostazioni → Stampanti** seleziona la stampante e apri la sua sottoscheda **Telecamera**.
2. Attiva **Usa la telecamera (voce «Mostra telecamera» nel menu dell’area di notifica)**.
3. Inserisci l’**Indirizzo dello stream RTSP**, ad es. `rtsp://192.168.1.21/live` per la telecamera Buddy3D (la porta
   predefinita è 554). Non inserire nome utente o password nell’indirizzo.
4. Fai clic su **Verifica telecamera**. Se la telecamera funziona, vengono mostrate un’istantanea e la sua risoluzione.
5. Fai clic su **Salva** e scegli **Mostra telecamera** nel sottomenu della stampante.

Come per l'indirizzo della stampante, anche un indirizzo della telecamera che può trovarsi fuori dalla rete locale
mostra un avviso: RTSP non è cifrato, quindi l'immagine della telecamera potrebbe essere intercettata lungo il percorso.
Il salvataggio resta possibile; meglio usare una VPN. Lo stesso link accanto apre il capitolo [Accesso sicuro a
PrusaLink](#14-accesso-sicuro-a-prusalink).

### 3.4 Immagine della stampante

L’elenco delle stampanti mostra un’immagine di ciascuna stampante, scelta automaticamente in base al modello. Fai clic
sull’immagine per scegliere un altro modello, un’illustrazione generica o un’immagine tua (PNG, JPG).

## 4. L’icona e il suo menu

### 4.1 Cosa mostra l’icona

L’icona mostra lo stato più importante tra tutte le tue stampanti:

| Stato | Significato |
|---|---|
| **Pronta** | la stampante è inattiva |
| **In stampa** | l’icona si riempie man mano che la stampa avanza |
| **In pausa** | la stampa è in pausa |
| **Completata** / **Interrotta** | la stampa è terminata |
| **Richiede attenzione** | la stampante ti aspetta (ad es. per un cambio filamento o una richiesta sul display) |
| **Errore** | la stampante segnala un errore |
| **Offline** | la stampante non è raggiungibile |
| **Credenziali non valide** | la stampante ha rifiutato il nome utente o la password |

Passa il mouse sopra l’icona per vedere i dettagli nel suo tooltip: avanzamento, tempo rimanente e nome del file.
Mentre la telecamera di una stampante sta registrando, il tooltip indica **Registrazione in corso**.

### 4.2 Il menu

- **Windows:** clic destro sull’icona.
- **macOS:** clic sull’icona nella barra dei menu.

Il menu contiene:

- **Un sottomenu per ogni stampante**, con nell’intestazione il nome, lo stato e l’avanzamento della stampante, e con:
  - **Mostra telecamera** / **Nascondi telecamera**, e **Porta la telecamera in primo piano** mentre la finestra è
    aperta,
  - **Registra** / **Interrompi registrazione**, **Riproduci ultima registrazione**, **Apri la cartella delle
    registrazioni**,
  - **Salva gli ultimi 2 min** (quando la dashcam è attiva, vedi [8.3](#83-dashcam-gli-ultimi-minuti)),
  - le voci del timelapse (sperimentale, vedi [8.4](#84-timelapse-sperimentale)),
  - **Apri PrusaLink**: apre l’interfaccia web PrusaLink della stampante nel browser, con l’accesso già effettuato. La
    tua password non viene mai passata al browser.
- **Diagnostica…** (vedi [10](#10-diagnostica))
- **Impostazioni…**
- **Nuova versione X.Y.Z disponibile** (solo quando è disponibile un aggiornamento, vedi [12](#12-aggiornamenti))
- **Manuale utente**: apre questo manuale
- **Informazioni**
- **Esci**

Prima che tu aggiunga la prima stampante, il menu offre **Aggiungi stampante…** al posto dei sottomenu delle stampanti.

## 5. Finestra della telecamera

<p align="center">
  <img src="../../docs/images/it/camera-window.jpg" alt="Finestra della telecamera durante una stampa con i widget di stato, nome della stampa, tempi e temperature" width="860">
</p>

Aprila con **Mostra telecamera** nel sottomenu della stampante. La finestra mostra l’immagine dal vivo della telecamera
con sopra i [widget](#6-widget). I pulsanti della finestra compaiono quando ci passi sopra con il mouse.

### 5.1 Tre modalità

| Modalità | Come passarci | A cosa serve |
|---|---|---|
| **Finestra normale** | pulsante nella finestra, oppure doppio clic nell’anteprima mini | per guardare con calma, a qualsiasi dimensione e su qualsiasi monitor |
| **Anteprima mini** | pulsante **Anteprima piccola, sempre in primo piano** | una finestrella che resta sopra le altre finestre per tutto il giorno |
| **Schermo intero** | **Windows:** F11 · **macOS:** ⌃⌘F · oppure doppio clic sull’immagine; **Esc** per tornare indietro | tutto lo schermo |

L’anteprima mini ha una barra informativa sotto l’immagine (avanzamento, tempo rimanente, ora di fine). Due pulsanti
aprono altre righe: il **nome della stampa** e il **grafico delle temperature**. Cosa mostra la barra si imposta nella
sottoscheda **Finestra della telecamera** della stampante (vedi [11.1](#111-stampanti)).

Per risparmiare CPU, l’anteprima mini mostra per impostazione predefinita al massimo 10 fotogrammi al secondo, più che
sufficienti per l’immagine piccola; la finestra normale e lo schermo intero mostrano ogni fotogramma della telecamera. Il
limite si imposta nella scheda **Telecamera** delle impostazioni (vedi [11.6](#116-telecamera)).

<p align="center">
  <img src="../../docs/images/it/camera-mini.jpg" alt="Anteprima mini con la barra informativa, la riga del nome della stampa e la riga del grafico delle temperature" width="400">
</p>

### 5.2 Sempre in primo piano

Il pulsante con la puntina nella finestra mostra se la finestra resta sopra le altre finestre e lo cambia con un solo
clic. La stessa opzione si trova nel menu contestuale della finestra (**Sempre in primo piano**).

### 5.3 Menu contestuale della finestra

Fai clic destro sull’immagine (su macOS anche Ctrl-clic) per **Modifica layout**, **Sempre in primo piano**, la
registrazione, la dashcam e il timelapse.

### 5.4 Quando la connessione cade

La finestra si riconnette da sola quando la telecamera o il Wi-Fi si interrompono; nel frattempo mostra **Connessione
alla telecamera…** o **Nessun segnale**. Riprova dopo 5 secondi; finché la telecamera resta irraggiungibile, la pausa
raddoppia fino a un minuto. La finestra indica all'incirca quando arriva il prossimo tentativo e lo tiene aggiornato;
aprire la finestra, ripristinarla dopo averla ridotta a icona o fare clic su **Verifica telecamera** nelle impostazioni
riprova subito. Quando la stampante viene spenta, la finestra si chiude e si riapre nello stesso punto quando la
stampante torna disponibile. Puoi disattivarlo in **Impostazioni → Stampanti → Finestra della telecamera** (**Chiudi
quando la stampante si spegne, riapri quando si riaccende**). Una finestra che chiudi tu resta chiusa.

## 6. Widget

I widget sono le schede sopra l’immagine della telecamera. Ogni stampante ha il proprio set e la propria disposizione.

| Widget | Mostra |
|---|---|
| **Stato** | nome della stampante, stato e avanzamento in percentuale |
| **Tempi** | tempo rimanente, quando la stampa sarà pronta, tempo di stampa |
| **Temperature** | ugello e piano (attuale / obiettivo) con un grafico degli ultimi 60 minuti |
| **Nome della stampa** | nome del file leggibile con materiale, altezza dello strato e ugello; le dimensioni più grandi aggiungono l’anteprima del G-code |
| **Raffreddamento** | dopo la stampa: quanto si sono raffreddati ugello e piano e quando puoi spegnere in sicurezza |

Ogni widget ha tre dimensioni: **S**, **M** e **L**. Qui le temperature: S mostra i valori, M aggiunge il grafico, L le
etichette e l’intervallo di temperatura.

<p align="center">
  <img src="../../docs/images/it/widget-sizes.jpg" alt="Il widget Temperature nelle dimensioni S, M e L" width="860">
</p>

### 6.1 Scegliere i widget

In **Impostazioni → Widget** scegli la stampante in alto e spunta i widget che vuoi. Selezionando un widget compaiono le
sue impostazioni, ad es. le soglie di raffreddamento dell’ugello e del piano. **Usa il layout per tutte le stampanti**
copia la disposizione sulle altre stampanti. **Opacità dello sfondo delle schede** mantiene il testo leggibile su
un’immagine chiara (per tutte le stampanti).

<p align="center">
  <img src="../../docs/images/it/settings-widgets.png" alt="Impostazioni, scheda Widget" width="620">
</p>

### 6.2 Disporre i widget nella finestra della telecamera

Fai clic su **Modifica layout** nella finestra della telecamera (oppure su **Modifica layout nella finestra…** in
**Impostazioni → Widget**). In modalità layout:

- **trascina** un widget in uno dei sei posti (in alto e in basso, a sinistra, al centro e a destra),
- fai clic su un widget per aprirne le opzioni: **Dimensione** (Piccola, Media, Grande) ed **Evidenzia** (mostra il
  valore principale nel colore di risalto),
- **Ripristina predefiniti** riporta la disposizione predefinita, **Fine** la salva.

Con la tastiera: **Spazio** prende un widget, i **tasti freccia** lo spostano, **Invio** ne apre le opzioni ed **Esc**
annulla le modifiche. Se la finestra è troppo piccola, ingrandiscila prima.

<p align="center">
  <img src="../../docs/images/it/camera-layout-mode.jpg" alt="Modalità layout con le opzioni di dimensione del widget Temperature" width="860">
</p>

## 7. Notifiche

LayerPeek mostra notifiche native del sistema per questi eventi (ognuno si può attivare o disattivare in
**Impostazioni → Generale**):

- stampa avviata, completata, messa in pausa, ripresa e interrotta,
- la stampante richiede attenzione (anche in caso di password rifiutata), un errore,
- la stampante è andata offline o è di nuovo online,
- **Mancano N minuti**, così puoi essere alla stampante quando la stampa finisce (**Avvisa quando mancano questi minuti
  di stampa (0 = disattivato)**),
- **Stampante raffreddata**: ugello e piano sono sotto le soglie del widget **Raffreddamento** e puoi spegnere la
  stampante in sicurezza.

Il **Suono** può essere disattivato, attivo solo per le notifiche importanti o attivo per tutte le notifiche. **Invia
notifica di prova** mostra un esempio, così puoi verificare che le notifiche ti arrivino.

- **Windows:** se le notifiche di LayerPeek sono disattivate in **Impostazioni → Sistema → Notifiche**, le impostazioni
  di LayerPeek lo segnalano e offrono **Apri impostazioni notifiche**. Controlla anche **Non disturbare**.
- **macOS:** la prima notifica chiede l’autorizzazione; fai clic su **Consenti**. Se le notifiche sono disattivate in
  **Impostazioni di Sistema → Notifiche**, le impostazioni di LayerPeek offrono **Apri impostazioni Notifiche**.
  Controlla anche **Full immersion** (Focus).

Un clic su una notifica della stampante apre l’interfaccia web PrusaLink della stampante. Un allarme di errore di stampa
apre la finestra della telecamera, una registrazione non riuscita apre la scheda **Riprese** delle impostazioni e
una conversione timelapse completata apre la cartella delle registrazioni.

## 8. Registrazione, dashcam e timelapse

Tutte le registrazioni restano sul tuo disco. Per impostazione predefinita vanno nella cartella **Video** (Windows) o
**Filmati** (macOS), sotto `LayerPeek`, con una sottocartella per ogni stampante e un nome che riporta l’ora e la
stampa. Il video viene salvato come MP4 senza ricodifica, quindi non pesa quasi per niente sulla CPU.

<p align="center">
  <img src="../../docs/images/it/camera-recording.jpg" alt="Parte superiore della finestra della telecamera con gli indicatori TL e REC" width="860">
</p>

Mentre la telecamera registra, la finestra della telecamera mostra un **REC** rosso; mentre si crea un timelapse, un
**TL** ambra.

### 8.1 Registrare a mano

**Registra** / **Interrompi registrazione** nel sottomenu della stampante, oppure il pulsante di registrazione nella
finestra della telecamera. **Riproduci ultima registrazione** e **Apri la cartella delle registrazioni** si trovano nel
sottomenu della stampante.

### 8.2 Registrare ogni stampa

Attiva **Registra ogni stampa** nella sottoscheda **Telecamera** della stampante. Ogni stampa viene
registrata dall’inizio fino a un minuto dopo la fine, anche senza una finestra della telecamera aperta.

<p align="center">
  <img src="../../docs/images/it/settings-camera.png" alt="Impostazioni, sottoscheda Telecamera di una stampante con registrazione automatica, dashcam e timelapse" width="620">
</p>

### 8.3 Dashcam: gli ultimi minuti

Attiva **Conserva gli ultimi minuti (dashcam)** nella sottoscheda **Telecamera** della stampante. Durante una stampa
LayerPeek tiene in memoria gli ultimi 2 minuti della telecamera (circa 8 MB al minuto per stampante); sul disco non
viene scritto nulla finché non li salvi. Gli ultimi minuti, insieme ai 30 secondi successivi all’evento, vengono
salvati:

- automaticamente in caso di errore di stampa, quando la stampante richiede attenzione, quando la stampa viene
  interrotta e quando il rilevamento degli errori di stampa lancia un allarme (ognuno si può disattivare nella scheda
  **Riprese**),
- a mano con **Salva gli ultimi 2 min** nel sottomenu della stampante o nel menu contestuale della finestra della
  telecamera.

La durata (1–10 minuti) si imposta nella scheda **Riprese**.

### 8.4 Timelapse (sperimentale)

Un timelapse è un breve video dell’intera stampa: per impostazione predefinita un fotogramma ogni 30 secondi, nessuno
mentre la stampa è in pausa, riprodotto a 30 fotogrammi al secondo. Viene salvato come `…_timelapse.mp4` accanto alle
registrazioni.

- **A mano:** **Avvia timelapse (sperimentale)** / **Interrompi timelapse (sperimentale)** nel sottomenu della stampante
  oppure il pulsante con il cronometro nella finestra della telecamera. **Riproduci ultimo timelapse** si trova nel
  sottomenu.
- **Ogni stampa:** **Crea un timelapse delle stampe** nella sottoscheda **Telecamera** della stampante.
- **Intervallo:** nella scheda **Riprese**, da 5 a 600 secondi, oppure passa a **Ogni N strati (stima)**: un
  fotogramma quando inizia un nuovo strato. PrusaLink non riporta gli strati, quindi LayerPeek li stima dall’altezza
  dell’asse Z.
- **Crea timelapse da una registrazione… (sperimentale)** crea un timelapse da una registrazione di LayerPeek già
  esistente.
- **Esporta timelapse in JPEG… (sperimentale)** salva i fotogrammi di un timelapse come file JPEG numerati, ad es. per
  il tuo montaggio video.

Entrambe le conversioni mostrano l’avanzamento nella voce di menu, dove puoi anche annullarle, e una notifica ti avvisa
quando sono terminate.

### 8.5 Cartella e limite di dimensione

Nella scheda **Riprese** puoi cambiare la **Cartella delle registrazioni** e il **Limite di dimensione di tutte le
registrazioni (GB)** (50 GB per impostazione predefinita). Oltre il limite vengono eliminate le registrazioni più
vecchie; con meno di 5 GB di spazio libero sull’unità la registrazione si interrompe. Una registrazione non riuscita
mostra una notifica.

<p align="center">
  <img src="../../docs/images/it/settings-recording.png" alt="Impostazioni, scheda Riprese" width="620">
</p>

## 9. Rilevamento degli errori di stampa (sperimentale)

LayerPeek può sorvegliare una stampa attraverso la telecamera e avvisarti quando qualcosa va storto sul piano. Funziona
solo sul tuo computer; l’immagine della telecamera non lo lascia mai.

<p align="center">
  <img src="../../docs/images/it/settings-detection.jpg" alt="Impostazioni, sottoscheda Rilevamento con l’area del piano sopra l’immagine della telecamera" width="620">
</p>

### 9.1 Attivarlo

È disattivato per impostazione predefinita e va attivato in due punti:

1. **Cerca errori di stampa nell’immagine della telecamera** nella scheda **Riprese** (per tutte le stampanti),
2. **Sorveglia le stampe di questa stampante con la telecamera** nella sottoscheda **Rilevamento** della stampante.

La stampante deve avere la telecamera attivata. Il rilevamento funziona solo durante la stampa e con la luce della
camera accesa, anche senza una finestra della telecamera aperta.

### 9.2 Area del piano

L’**Area del piano** è la parte dell’immagine che viene sorvegliata. Trascina un nuovo rettangolo, spostalo o cambiane
la dimensione da un angolo con il mouse, oppure usa i tasti freccia (Maiusc + frecce ne cambiano la dimensione).
**Immagine intera** sorveglia tutta l’immagine, **Carica immagine** prende una nuova immagine dalla telecamera e, per una
CORE One, **Predefinita per Core One** ripristina la sua impostazione predefinita.

### 9.3 Rilevatori

- **Cambiamento sul piano** nota un pezzo che è sparito o si è spostato e nuovo materiale sul piano (spaghetti, un pezzo
  staccato). **Filamento scuro** aiuta con filamento nero o molto scuro, al prezzo di più falsi allarmi.
- **Stampa nel vuoto** (disattivato per impostazione predefinita, solo per stampanti il cui piano scende durante la
  stampa: CORE One, CORE One L, XL) nota che la stampante continua a stampare ma non cresce più nulla, ad es. quando il
  filamento è finito.

**Sensibilità** (0–100, 50 per impostazione predefinita): più alta significa più allarmi, anche falsi; più bassa
significa meno allarmi, ma un errore potrebbe sfuggire.

### 9.4 L’allarme

L’allarme è una notifica **Possibile errore di stampa – …** con un’istantanea della telecamera. Un clic sulla notifica
apre la finestra della telecamera. Il pulsante **Falso allarme** conserva una registrazione locale per la messa a punto;
nulla lascia il tuo computer. Nella scheda **Riprese** imposti ogni quanto si ripete l’allarme, se salvare
un’istantanea di ogni allarme e ogni quanto viene controllata la telecamera (da 2 a 5 secondi).

> [!WARNING]
> Il rilevamento non nota la qualità del primo strato, i problemi all’ugello o i difetti all’interno del pezzo. Può non
> accorgersi di un errore o lanciare un falso allarme. Non affidarti a esso come unica protezione di una stampa senza
> sorveglianza.

## 10. Diagnostica

**Diagnostica…** nel menu dell’icona (scegli la stampante in alto nella finestra) mostra il modello e il firmware della
stampante, lo stato attuale, le temperature dell’ugello e del piano (attuale e obiettivo), le ventole, la velocità di
stampa, il flusso e l’altezza Z, e un grafico delle temperature degli ultimi 30 minuti, 2 ore o 24 ore. La cronologia
viene conservata solo in memoria e parte all’avvio di LayerPeek.

<p align="center">
  <img src="../../docs/images/it/diagnostics.png" alt="Finestra Diagnostica con la cronologia delle temperature" width="620">
</p>

## 11. Guida alle impostazioni

Apri **Impostazioni…** dal menu dell’icona. Le modifiche si applicano dopo **Salva**; **Annulla** le scarta.

### 11.1 Stampanti

L’elenco delle stampanti con **Aggiungi stampante** e **Rimuovi**. Ogni stampante ha delle sottoschede:

- **Stampante:** nome, indirizzo, nome utente, password, **Verifica connessione**.
- **Telecamera:** l’indirizzo RTSP, **Verifica telecamera**, registrazione automatica, dashcam e timelapse delle stampe.
- **Finestra della telecamera:** **Apri come** (come l’ultima volta, finestra normale, anteprima mini, schermo intero),
  il monitor, la dimensione, la posizione e **Sempre in primo piano** della finestra normale e dell’anteprima mini,
  **Ricorda l’ultima posizione e dimensione**, **Chiudi quando la stampante si spegne, riapri quando si riaccende** e
  cosa mostra la barra informativa dell’anteprima mini.
- **Rilevamento:** il rilevamento degli errori di stampa di questa stampante (vedi
  [9](#9-rilevamento-degli-errori-di-stampa-sperimentale)).

### 11.2 Widget

I widget di ogni stampante, le loro impostazioni e l’opacità delle schede (vedi [6](#6-widget)).

### 11.3 Riprese

La cartella e il limite di dimensione delle registrazioni, la dashcam, il timelapse e il rilevamento degli errori di
stampa (vedi [8](#8-registrazione-dashcam-e-timelapse) e [9](#9-rilevamento-degli-errori-di-stampa-sperimentale)). Le
funzioni sperimentali hanno l’etichetta **Sperimentale**.

### 11.4 Generale

- **Notifiche:** quali eventi, **Suono**, il promemoria «minuti rimanenti», **Invia notifica di prova**.
- **Intervallo di interrogazione (secondi):** ogni quanto alle stampanti viene chiesto il loro stato.
- **Lingua:** **Lingua di sistema** oppure una tra Čeština, Deutsch, English, Español, Français, Italiano, Polski,
  Slovenčina. Cambia solo i testi; numeri, date e orari seguono le impostazioni internazionali del tuo sistema.
- **Registro dettagliato (Debug):** attivalo solo quando ti viene chiesto durante la risoluzione di un problema.

### 11.5 Avvio e aggiornamenti

- **Windows:** **Avvia all’accesso a Windows**. Attivo per impostazione predefinita; disponibile solo nella versione
  installata, non nella versione ZIP.
- **macOS:** **Apri al login**. Disattivato per impostazione predefinita; disponibile solo quando LayerPeek si trova
  nella cartella Applicazioni. Se hai disattivato LayerPeek in **Impostazioni di Sistema → Generali → Elementi login ed
  estensioni** (Login Items & Extensions), solo tu puoi riattivarlo da lì; il pulsante **Apri Impostazioni di Sistema**
  ti ci porta.
- **Controlla automaticamente gli aggiornamenti**, **Offri versioni preliminari** e **Controlla ora** (vedi
  [12](#12-aggiornamenti)).

### 11.6 Telecamera

Impostazioni di tutte le telecamere (la telecamera di ogni stampante si imposta nella sua sottoscheda **Telecamera**,
vedi [11.1](#111-stampanti)).

- **Frequenza fotogrammi dell’anteprima mini:** quanti fotogrammi al secondo mostra l’anteprima mini. **Risparmio**
  (5 fps), **Bilanciato** (10 fps, predefinito) o **Fluido** (ogni fotogramma della telecamera); con il cursore imposti un
  altro limite da 1 a 30 fps (**Personalizzato**). Meno fotogrammi significano meno carico di CPU e meno dati copiati; la
  telecamera viene comunque letta per intero, quindi registrazioni, timelapse e rilevamento degli errori di stampa non
  cambiano. La finestra normale e lo schermo intero mostrano sempre ogni fotogramma. La modifica vale per un’anteprima
  mini aperta dopo **Salva**.

## 12. Aggiornamenti

Poco dopo l’avvio e poi una volta al giorno LayerPeek chiede a GitHub se è uscita una nuova versione (puoi disattivare
questo controllo). Una nuova versione compare come **Nuova versione X.Y.Z disponibile** nel menu dell’icona e una volta
come notifica.

- **Windows, versione installata:** la voce di menu scarica l’aggiornamento, chiude LayerPeek e avvia la nuova versione.
- **Windows, versione ZIP:** la voce di menu apre la pagina della release; scarica a mano il nuovo ZIP.
- **Windows, installata con winget:** usa anche qui la voce di menu. Anche `winget upgrade` (pure all’interno di
  `winget upgrade --all`) funziona, ma chiude bruscamente LayerPeek se è in esecuzione, non lo riavvia (avvialo dal menu
  Start) ed elimina i file di log in `%LOCALAPPDATA%\LayerPeek\logs`.
- **macOS:** la voce di menu scarica l’aggiornamento e riavvia LayerPeek, ma solo quando l’app si trova in
  **Applicazioni** (non sull’immagine disco o in Download).

Le tue impostazioni e password vengono mantenute.

**Aggiornamento rifiutato:** la versione installata accetta un aggiornamento solo se è possibile verificarne la firma.
Se non è possibile, LayerPeek non installa nulla: mostra una notifica (una volta per ogni versione offerta; un clic apre
le impostazioni) e un errore nella scheda **Avvio e aggiornamenti**, e il menu dell’icona mostra **Aggiornamento
rifiutato…** al posto della nuova versione (la notifica della nuova versione viene rimossa). Resta così finché un
controllo successivo non riesce: allora la nuova versione viene offerta di nuovo e la sua notifica compare ancora una
volta. Non installare quella versione a mano, la release stessa potrebbe essere stata manomessa: attendi la prossima
versione o segui gli annunci del progetto.

## 13. Dove vengono salvati i tuoi dati

| Cosa | Windows | macOS |
|---|---|---|
| Impostazioni | `%APPDATA%\LayerPeek\settings.json` | `~/Library/Application Support/LayerPeek/settings.json` |
| Password | Gestione credenziali di Windows | portachiavi «login» |
| Log | `%LOCALAPPDATA%\LayerPeek\logs` | `~/Library/Logs/LayerPeek` |
| Registrazioni | `Videos\LayerPeek` | `Movies/LayerPeek` |

La cronologia delle temperature viene conservata solo in memoria. LayerPeek si connette solo alle stampanti e alle
telecamere che aggiungi e, a meno che tu non disattivi il controllo degli aggiornamenti, a GitHub (l’elenco delle
release, senza alcun account, e il download di un aggiornamento che avvii tu). Non c’è alcuna telemetria.

## 14. Accesso sicuro a PrusaLink

PrusaLink sulla stampante parla solo **HTTP** non cifrato; la stampante stessa non offre HTTPS. Prusa progetta PrusaLink
per la rete locale e sconsiglia di raggiungerlo da internet tramite un port forwarding sul router; consiglia invece una
VPN cifrata. In una rete domestica che usi solo tu, di solito basta. Continua a leggere se altre persone o dispositivi
condividono la rete tra il tuo computer e la stampante (Wi-Fi condiviso, ufficio, laboratorio, studentato) o se vuoi
raggiungere la stampante da fuori.

Un indirizzo della stampante `http://` o un indirizzo della telecamera che può trovarsi fuori dalla rete locale riceve
un avviso nelle impostazioni (vedi [3.2 Aggiungere la stampante](#32-aggiungere-la-stampante)); il salvataggio resta
possibile.

### 14.1 In cosa consiste il rischio

- **Chiunque possa vedere il traffico** tra il computer e la stampante può leggerlo: lo stato della stampante, i nomi
  dei file e il G-code caricato tramite l’interfaccia web PrusaLink.
- **L’accesso:** LayerPeek accede con HTTP Digest, l’unico accesso che la stampante offre oltre alla chiave API. La
  password in sé non viene inviata, ma il Digest della stampante è la vecchia variante con MD5 (RFC 2069). Da un accesso
  intercettato un attaccante può provare a indovinare la password offline e, per un massimo di 5 minuti, ripetere la
  stessa richiesta intercettata. Il Digest non protegge il contenuto: chi può modificare il traffico può modificare i
  dati.
- **La chiave API:** gli strumenti che accedono con la chiave API (l’intestazione `X-Api-Key`) inviano la password
  PrusaLink stessa, leggibile da chiunque ascolti. In PrusaSlicer scegli quindi **HTTP digest** invece della chiave API.
- **Con la password** chiunque controlla la stampante tramite PrusaLink: avvia e interrompe le stampe, carica ed elimina
  file.

### 14.2 La password

La stampante genera la password da sola; il firmware attuale la compone di 15 caratteri casuali tra 57 lettere e cifre
(senza 0, O, 1, l e I, facili da confondere). Indovinare una password così da un accesso intercettato non è fattibile in
pratica. Sulle stampanti con firmware Buddy non puoi inserire una password tua, puoi solo generarne una nuova.

Genera una nuova password ogni volta che la vecchia potrebbe essere trapelata (una foto condivisa del display o del
codice QR di PrusaLink, che contiene la password, oppure la chiave API usata in una rete non affidabile):

1. Sulla stampante apri **Impostazioni → Rete → PrusaLink** (Settings → Network → PrusaLink) e scegli **Genera
   password** (Generate Password).
2. In LayerPeek apri **Impostazioni → Stampanti**, seleziona la stampante, inserisci la nuova password in **Password**
   nella sottoscheda **Stampante**, fai clic su **Verifica connessione** e poi su **Salva**.
3. Cambiala anche negli altri strumenti che usano PrusaLink (PrusaSlicer e altri).

### 14.3 Una rete separata e il firewall

- Metti stampanti e telecamere in una **rete separata** del router (per i dispositivi IoT, una VLAN). Consenti al
  computer con LayerPeek di raggiungerle: la porta TCP **80** della stampante (PrusaLink) e la porta RTSP della
  telecamera (**554** di default). Le reti ospiti spesso bloccano le connessioni tra dispositivi; consulta il manuale
  del router.
- **Non inoltrare mai una porta del router** alla stampante. Per raggiungerla da fuori, collegati alla rete di casa
  tramite una VPN.
- LayerPeek di per sé non offre nulla alla rete: **Apri PrusaLink** è in ascolto solo su `127.0.0.1` del tuo computer.
  Il firewall del computer non ha bisogno di nuove regole per LayerPeek.

### 14.4 HTTPS tramite un reverse proxy

Un piccolo computer accanto alla stampante (per esempio un Raspberry Pi) esegue un reverse proxy, **Caddy** o **nginx**.
LayerPeek comunica con il proxy in HTTPS; solo il proxy parla con la stampante in HTTP non cifrato. Collega proxy e
stampante con un cavo corto o allo stesso switch e, se il router lo consente, lascia che solo il proxy raggiunga la
stampante.

- In LayerPeek inserisci come indirizzo della stampante quello del proxy, ad es. `https://printer.home.arpa`. Il nome
  deve portare al proxy (una voce DNS nel router o il file `hosts`).
- Usa **un nome host o una porta per ogni stampante**, non un percorso come `https://proxy/mk4/`. La stampante verifica
  l’accesso in base al percorso della richiesta, quindi con un proxy che rimuove un tale prefisso la stampante rifiuta
  la password.
- La telecamera (RTSP) non passa dal proxy; LayerPeek si collega direttamente a essa.

**Caddy** (il file `Caddyfile`, `192.168.1.20` è la stampante):

```caddyfile
printer.home.arpa {
	tls internal
	reverse_proxy 192.168.1.20
}
```

Con `tls internal` Caddy emette il certificato dalla propria autorità di certificazione (CA). Rendi attendibile il suo
certificato radice sul computer (vedi [14.5](#145-rendere-attendibile-il-certificato-sul-computer)); quando Caddy gira
come servizio Linux, il file è `/var/lib/caddy/.local/share/caddy/pki/authorities/local/root.crt`. Caddy inoltra il
percorso e le intestazioni di accesso senza modificarli.

**nginx** (un blocco `server`, ad es. in `/etc/nginx/conf.d/printer.conf`):

```nginx
server {
    listen 443 ssl;
    server_name printer.home.arpa;
    ssl_certificate     /etc/nginx/tls/printer.crt;
    ssl_certificate_key /etc/nginx/tls/printer.key;
    client_max_body_size 0;

    location / {
        proxy_pass http://192.168.1.20;
    }
}
```

`proxy_pass` senza percorso inoltra il percorso senza modificarlo. `client_max_body_size 0` elimina il limite di 1 MB,
così il caricamento di G-code tramite l’interfaccia web PrusaLink funziona. nginx ha bisogno di un certificato della tua
CA, creato per esempio con OpenSSL:

```sh
openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=Printer proxy CA" \
  -addext "basicConstraints=critical,CA:TRUE" -addext "keyUsage=critical,keyCertSign,cRLSign" \
  -keyout ca.key -out ca.crt
openssl req -newkey rsa:3072 -nodes -subj "/CN=printer.home.arpa" -keyout printer.key -out printer.csr
printf "subjectAltName=DNS:printer.home.arpa\nextendedKeyUsage=serverAuth\n" > printer.ext
openssl x509 -req -in printer.csr -CA ca.crt -CAkey ca.key -CAcreateserial -days 825 \
  -extfile printer.ext -out printer.crt
```

Copia `printer.crt` e `printer.key` sul proxy e `ca.crt` sui tuoi computer; conserva `ca.key` al sicuro e fuori dalla
rete. Il certificato del server vale 825 giorni, il massimo che macOS accetta.

### 14.5 Rendere attendibile il certificato sul computer

LayerPeek accetta solo certificati che il tuo sistema considera attendibili. Un certificato autofirmato, o uno di una CA
che il sistema non conosce, fallisce: **Verifica connessione** segnala *«La connessione sicura (https) non è riuscita.
Prova l'indirizzo con http:// o controlla il certificato della stampante.»*, la stampante risulta **Offline** e
**Diagnostica** mostra *«La connessione sicura (https) alla stampante non è riuscita.»* LayerPeek non può rendere
attendibile da sé un singolo certificato; aggiungi al sistema il certificato della tua CA (`ca.crt` o il `root.crt` di
Caddy):

- **Windows:** premi **Win+R**, esegui `certmgr.msc`, fai clic con il tasto destro su **Autorità di certificazione
  radice attendibili → Certificati** (Trusted Root Certification Authorities → Certificates), scegli **Tutte le attività
  → Importa…** e importa il file in **Autorità di certificazione radice attendibili**; conferma l’avviso di sicurezza.
  Oppure in un terminale: `certutil -user -addstore Root ca.crt`.
- **macOS:** apri **Accesso Portachiavi** (con Spotlight), seleziona il portachiavi **login** e trascina il file nella
  finestra. Fai doppio clic sul certificato, apri **Fiducia** e imposta **Quando si utilizza questo certificato** su
  **Fidati sempre** (Always Trust); conferma con la tua password.

Poi esegui di nuovo **Verifica connessione**.

Fonti: PrusaLink [nella rete locale](https://help.prusa3d.com/article/prusa-connect-and-prusalink-explained_302608),
[port forwarding e VPN](https://help.prusa3d.com/article/prusalink-sl1-sl1s_146094), [HTTP digest in
PrusaSlicer](https://help.prusa3d.com/article/prusalink-troubleshooting_304411); firmware: [porta 80 senza
TLS](https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/7d8bf6858b363c453c0617bceab5e66997d8fc25/lib/WUI/http_lifetime.cpp),
[Digest e chiave
API](https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/7d8bf6858b363c453c0617bceab5e66997d8fc25/lib/WUI/nhttp/req_parser.cpp),
[validità del
nonce](https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/7d8bf6858b363c453c0617bceab5e66997d8fc25/src/common/http/types.h),
[generazione della
password](https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/7d8bf6858b363c453c0617bceab5e66997d8fc25/lib/WUI/wui.cpp)
e
[lunghezza](https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/7d8bf6858b363c453c0617bceab5e66997d8fc25/src/persistent_stores/store_instances/config_store/constants.hpp),
[menu
PrusaLink](https://github.com/prusa3d/Prusa-Firmware-Buddy/blob/7d8bf6858b363c453c0617bceab5e66997d8fc25/src/gui/screen_prusa_link.cpp);
[RFC 2069](https://www.rfc-editor.org/rfc/rfc2069); [Caddy
`reverse_proxy`](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy),
[`tls`](https://caddyserver.com/docs/caddyfile/directives/tls), [directory dei
dati](https://caddyserver.com/docs/running); [nginx
`proxy_pass`](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_pass),
[`client_max_body_size`](https://nginx.org/en/docs/http/ngx_http_core_module.html#client_max_body_size); [requisiti di
Apple per i certificati](https://support.apple.com/en-us/103769), [Accesso
Portachiavi](https://support.apple.com/guide/keychain-access/change-the-trust-settings-of-a-certificate-kyca11871/mac);
[certutil](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/certutil).

## 15. Risoluzione dei problemi

**L’icona non è visibile (Windows).** Fai clic sulla freccia **`^`** nell’area di notifica e trascina l’icona di
LayerPeek sulla barra delle applicazioni.

**La stampante è Offline.** Verifica che la stampante sia accesa e collegata alla rete, che l’indirizzo sia corretto
(provalo in un browser) e che il tuo computer sia sulla stessa rete. Esegui **Verifica connessione** nelle impostazioni
della stampante.

**Credenziali non valide.** Inserisci di nuovo il nome utente e la password presi da **Impostazioni → Rete →
PrusaLink** sulla stampante e salva.

**La connessione sicura (https) non è riuscita.** L’indirizzo della stampante inizia con `https://`. O porta
direttamente alla stampante, che non offre HTTPS (usa `http://`), oppure il sistema non considera attendibile il
certificato del proxy: vedi [14.5](#145-rendere-attendibile-il-certificato-sul-computer).

**Nessuna connessione su macOS.** L’accesso alla rete locale potrebbe essere disattivato. In questo caso LayerPeek
offre **Apri Impostazioni di Sistema**; attiva LayerPeek in **Impostazioni di Sistema → Privacy e sicurezza → Rete
locale**.

**La telecamera mostra Nessun segnale.** Controlla l’indirizzo RTSP con **Verifica telecamera** e che la telecamera sia
accesa e raggiungibile. Non inserire nome utente o password nell’indirizzo.

**Le notifiche non compaiono.** Usa **Invia notifica di prova** in **Impostazioni → Generale** e controlla **Non
disturbare** (Windows) o **Full immersion** (macOS).

**Segnalare un bug.** Apri **Informazioni** dal menu dell’icona:

- **Salva pacchetto diagnostico…** salva un file ZIP con i log, la versione, le informazioni di sistema e le
  impostazioni, **senza password né chiavi**. Allegalo alla tua segnalazione.
- **Copia informazioni negli appunti** copia un breve rapporto.
- **Apri cartella dei registri** apre i log.

Se ti viene chiesto un log dettagliato, attiva **Registro dettagliato (Debug)** in **Impostazioni → Generale**, riproduci
il problema, salva il pacchetto e poi disattiva di nuovo il registro dettagliato.

Segnala bug e idee nelle [issue](https://github.com/pfoltynek/LayerPeek/issues) su GitHub. Indica il modello della
stampante, la versione del firmware e il sistema operativo.

## 16. Disinstallazione

- **Windows, versione installata:** **Impostazioni → App → App installate → LayerPeek → Disinstalla**.
- **Windows, versione ZIP:** chiudi LayerPeek ed elimina la sua cartella.
- **macOS:** chiudi LayerPeek (**Esci** nel suo menu) e sposta **LayerPeek** da **Applicazioni** al Cestino.

La disinstallazione mantiene impostazioni, password e registrazioni. Per rimuovere anche quelle, elimina le cartelle
indicate in [13](#13-dove-vengono-salvati-i-tuoi-dati) e le voci di LayerPeek in Gestione credenziali (Windows) o in
Accesso Portachiavi (macOS).
