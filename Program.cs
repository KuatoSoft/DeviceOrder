// DeviceOrder. Hide/show game controllers to isolate the Main device as "device 1".
// Validated approach: a controller is hidden by DISABLING its USB head node
// (USB\VID_xxxx&PID_yyyy\<instance>) via the Windows PnP cmdlets. 100% software, reversible.
//
// Generic: no controller model hardcoded. The user picks their own Main device
// (button "Mark as Main"). A pre-detection of known brands makes a suggestion.
// The choice (Main + language) is saved in %APPDATA%\DeviceOrder\config.txt.
//
// ABSOLUTE SAFETY RULES:
//   * NEVER disable a keyboard or a mouse (excluded by VID&PID).
//   * NEVER disable a device marked as "Main".
//   * Safety net: on close, re-enable everything that was disabled.
//   * No DLL injection into the game (anti-cheat). We stay at the system level (PnP).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DeviceOrder
{
    // === Localization (en, fr, de, es, it, pt-BR) ===
    internal static class L
    {
        // Language order: 0=en 1=fr 2=de 3=es 4=it 5=pt(BR)
        public static readonly string[] Codes = { "en", "fr", "de", "es", "it", "pt" };
        public static readonly string[] Names = { "English", "Français", "Deutsch", "Español", "Italiano", "Português (BR)" };
        public static int Lang = 0;

        private static readonly Dictionary<string, string[]> M = new Dictionary<string, string[]>
        {
            { "title", new[]{
                "DeviceOrder : Isolate your main device as device 1",
                "DeviceOrder : Isoler ton périphérique principal en device 1",
                "DeviceOrder : Hauptgerät als Gerät 1 isolieren",
                "DeviceOrder : Aislar tu dispositivo principal como dispositivo 1",
                "DeviceOrder : Isola la periferica principale come dispositivo 1",
                "DeviceOrder : Isolar seu dispositivo principal como dispositivo 1" } },
            { "header", new[]{
                "Quick guide:   1) Select your Main device and click \"Mark/unmark as Main\".   2) Set the order with Move up / Move down.   3) Click the first button to hide all except the Main; click it again to restore. Hover any button for a tip.",
                "Guide rapide :   1) Sélectionne ton périphérique principal et clique \"Marquer/démarquer comme principal\".   2) Définis l'ordre avec Monter / Descendre.   3) Clique le premier bouton pour cacher tout sauf le principal ; reclique pour restaurer. Survole un bouton pour une astuce.",
                "Kurzanleitung:   1) Hauptgerät auswählen und \"Als Hauptgerät markieren\" klicken.   2) Reihenfolge mit Nach oben / Nach unten festlegen.   3) Die erste Schaltfläche klicken, um alles außer dem Hauptgerät auszublenden; erneut klicken zum Wiederherstellen. Maus über eine Schaltfläche für einen Tipp.",
                "Guía rápida:   1) Selecciona tu dispositivo principal y pulsa \"Marcar/desmarcar como principal\".   2) Define el orden con Subir / Bajar.   3) Pulsa el primer botón para ocultar todo excepto el principal; vuelve a pulsar para restaurar. Pasa el ratón por un botón para ver una ayuda.",
                "Guida rapida:   1) Seleziona la periferica principale e clicca \"Segna/rimuovi come principale\".   2) Definisci l'ordine con Su / Giù.   3) Clicca il primo pulsante per nascondere tutto tranne la principale; clicca di nuovo per ripristinare. Passa il mouse su un pulsante per un suggerimento.",
                "Guia rápido:   1) Selecione seu dispositivo principal e clique \"Marcar/desmarcar como principal\".   2) Defina a ordem com Subir / Descer.   3) Clique no primeiro botão para ocultar tudo exceto o principal; clique de novo para restaurar. Passe o mouse sobre um botão para uma dica." } },
            { "colName", new[]{ "Controller", "Manette", "Gerät", "Mando", "Periferica", "Controle" } },
            { "colVidPid", new[]{ "VID & PID", "VID & PID", "VID & PID", "VID & PID", "VID & PID", "VID & PID" } },
            { "colState", new[]{ "State", "État", "Status", "Estado", "Stato", "Estado" } },
            { "stateActive", new[]{ "Active (visible)", "Active (visible)", "Aktiv (sichtbar)", "Activo (visible)", "Attivo (visibile)", "Ativo (visível)" } },
            { "stateHidden", new[]{ "Hidden (disabled)", "Cachée (désactivée)", "Ausgeblendet (deaktiviert)", "Oculto (desactivado)", "Nascosto (disattivato)", "Oculto (desativado)" } },
            { "keepTag", new[]{ "   [MAIN to keep]", "   [PRINCIPAL à garder]", "   [HAUPTGERÄT behalten]", "   [PRINCIPAL mantener]", "   [PRINCIPALE mantieni]", "   [PRINCIPAL manter]" } },
            { "btnIsolate", new[]{
                "Hide all except the Main", "Cacher tout sauf le principal", "Alles außer Hauptgerät ausblenden",
                "Ocultar todo excepto el principal", "Nascondi tutto tranne la principale", "Ocultar tudo exceto o principal" } },
            { "btnRestore", new[]{ "Restore all", "Tout restaurer", "Alle wiederherstellen", "Restaurar todo", "Ripristina tutto", "Restaurar tudo" } },
            { "btnRefresh", new[]{ "Refresh list", "Rafraîchir la liste", "Liste aktualisieren", "Actualizar lista", "Aggiorna elenco", "Atualizar lista" } },
            { "btnToggleWheel", new[]{
                "Mark/unmark selected as Main", "Marquer/démarquer comme principal", "Auswahl als Hauptgerät markieren",
                "Marcar/desmarcar como principal", "Segna/rimuovi come principale", "Marcar/desmarcar como principal" } },
            { "langLabel", new[]{ "Language:", "Langue :", "Sprache:", "Idioma:", "Lingua:", "Idioma:" } },
            { "statusReady", new[]{ "Ready.", "Prêt.", "Bereit.", "Listo.", "Pronto.", "Pronto." } },
            { "statusDetecting", new[]{ "Detecting controllers...", "Détection des manettes...", "Geräte werden erkannt...", "Detectando mandos...", "Rilevamento periferiche...", "Detectando controles..." } },
            { "statusDetected", new[]{ "{0} controller(s) detected.", "{0} manette(s) détectée(s).", "{0} Gerät(e) erkannt.", "{0} mando(s) detectado(s).", "{0} periferica/e rilevata/e.", "{0} controle(s) detectado(s)." } },
            { "statusNone", new[]{
                "No controller detected. Plug in your devices then Refresh.",
                "Aucune manette détectée. Branche tes périphériques puis Rafraîchir.",
                "Kein Gerät erkannt. Geräte anschließen, dann Aktualisieren.",
                "Ningún mando detectado. Conecta tus dispositivos y Actualiza.",
                "Nessuna periferica rilevata. Collega i dispositivi e Aggiorna.",
                "Nenhum controle detectado. Conecte os dispositivos e Atualize." } },
            { "statusShown", new[]{ "Shown: {0}", "Affichée : {0}", "Eingeblendet: {0}", "Mostrado: {0}", "Mostrato: {0}", "Exibido: {0}" } },
            { "statusHiddenOne", new[]{ "Hidden: {0}", "Cachée : {0}", "Ausgeblendet: {0}", "Oculto: {0}", "Nascosto: {0}", "Oculto: {0}" } },
            { "statusIsolated", new[]{
                "{0} controller(s) hidden. The Main is alone = device 1.",
                "{0} manette(s) cachée(s). Le principal est seul = device 1.",
                "{0} Gerät(e) ausgeblendet. Das Hauptgerät ist allein = Gerät 1.",
                "{0} mando(s) ocultado(s). El principal está solo = dispositivo 1.",
                "{0} periferica/e nascosta/e. La principale è da sola = dispositivo 1.",
                "{0} controle(s) ocultado(s). O principal está sozinho = dispositivo 1." } },
            { "failSuffix", new[]{ "  ({0} failure(s))", "  ({0} échec(s))", "  ({0} Fehler)", "  ({0} fallo(s))", "  ({0} errore/i)", "  ({0} falha(s))" } },
            { "statusRestored", new[]{
                "{0} controller(s) shown again.", "{0} manette(s) réaffichée(s).", "{0} Gerät(e) wieder eingeblendet.",
                "{0} mando(s) mostrado(s) de nuevo.", "{0} periferica/e mostrata/e di nuovo.", "{0} controle(s) exibido(s) novamente." } },
            { "msgWheelTitle", new[]{ "Main protected", "Principal protégé", "Hauptgerät geschützt", "Principal protegido", "Principale protetta", "Principal protegido" } },
            { "msgWheelBody", new[]{
                "The Main device must stay active: it's the one you want as device 1.\r\nTo isolate it, use the \"Hide all except the Main\" button instead.",
                "Le périphérique principal doit rester actif : c'est lui qu'on veut en device 1.\r\nPour l'isoler, utilise plutôt le bouton \"Cacher tout sauf le principal\".",
                "Das Hauptgerät muss aktiv bleiben: es soll Gerät 1 sein.\r\nZum Isolieren bitte \"Alles außer Hauptgerät ausblenden\" verwenden.",
                "El dispositivo principal debe seguir activo: es el que quieres como dispositivo 1.\r\nPara aislarlo, usa el botón \"Ocultar todo excepto el principal\".",
                "La periferica principale deve restare attiva: è quella che vuoi come dispositivo 1.\r\nPer isolarla, usa il pulsante \"Nascondi tutto tranne la principale\".",
                "O dispositivo principal deve permanecer ativo: é ele que você quer como dispositivo 1.\r\nPara isolá-lo, use o botão \"Ocultar tudo exceto o principal\"." } },
            { "msgRefusedTitle", new[]{ "Action refused", "Action refusée", "Aktion abgelehnt", "Acción rechazada", "Azione rifiutata", "Ação recusada" } },
            { "msgRefusedBody", new[]{
                "Failure on \"{0}\".\r\n\r\nPlease make sure the software that controls this device is closed, then try again.",
                "Échec sur \"{0}\".\r\n\r\nVeuillez vérifier que le logiciel qui contrôle ce périphérique est bien fermé, puis réessayez.",
                "Fehler bei \"{0}\".\r\n\r\nBitte stellen Sie sicher, dass die Software, die dieses Gerät steuert, geschlossen ist, und versuchen Sie es erneut.",
                "Fallo en \"{0}\".\r\n\r\nAsegúrate de que el software que controla este dispositivo esté cerrado y vuelve a intentarlo.",
                "Errore su \"{0}\".\r\n\r\nAssicurati che il software che controlla questa periferica sia chiuso, poi riprova.",
                "Falha em \"{0}\".\r\n\r\nVerifique se o software que controla este dispositivo está fechado e tente novamente." } },
            { "failHint", new[]{
                "  Close the software controlling that device, then retry.",
                "  Ferme le logiciel qui contrôle ce périphérique, puis réessaie.",
                "  Schließe die Software, die das Gerät steuert, und versuche es erneut.",
                "  Cierra el software que controla ese dispositivo y reinténtalo.",
                "  Chiudi il software che controlla quella periferica e riprova.",
                "  Feche o software que controla esse dispositivo e tente novamente." } },
            { "failNamed", new[]{
                "  Could not disable: {0}.", "  Impossible de désactiver : {0}.", "  Konnte nicht deaktivieren: {0}.",
                "  No se pudo desactivar: {0}.", "  Impossibile disattivare: {0}.", "  Não foi possível desativar: {0}." } },
            { "msgNoWheelTitle", new[]{ "No Main selected", "Pas de principal", "Kein Hauptgerät", "Sin principal", "Nessun principale", "Sem principal" } },
            { "msgNoWheelBody", new[]{
                "No device is marked as the Main. Select your Main device in the list and click \"Mark/unmark selected as Main\" first.",
                "Aucun périphérique n'est marqué comme principal. Sélectionne ton périphérique principal dans la liste puis clique \"Marquer/démarquer comme principal\".",
                "Kein Gerät ist als Hauptgerät markiert. Wähle dein Hauptgerät in der Liste und klicke zuerst \"Auswahl als Hauptgerät markieren\".",
                "Ningún dispositivo está marcado como principal. Selecciona tu dispositivo principal en la lista y pulsa \"Marcar/desmarcar como principal\".",
                "Nessun dispositivo è segnato come principale. Seleziona la periferica principale nell'elenco e clicca \"Segna/rimuovi come principale\".",
                "Nenhum dispositivo está marcado como principal. Selecione seu dispositivo principal na lista e clique em \"Marcar/desmarcar como principal\"." } },
            { "msgSelTitle", new[]{ "No selection", "Aucune sélection", "Keine Auswahl", "Sin selección", "Nessuna selezione", "Sem seleção" } },
            { "msgSelBody", new[]{
                "Select one or more controllers in the list first.",
                "Sélectionne d'abord une ou plusieurs manettes dans la liste.",
                "Wähle zuerst ein oder mehrere Geräte in der Liste.",
                "Selecciona primero uno o varios mandos en la lista.",
                "Seleziona prima una o più periferiche nell'elenco.",
                "Selecione primeiro um ou mais controles na lista." } },
            { "detectError", new[]{ "Detection error: {0}", "Erreur de détection : {0}", "Erkennungsfehler: {0}", "Error de detección: {0}", "Errore di rilevamento: {0}", "Erro de detecção: {0}" } },
            { "ttToggle", new[]{
                "Disables every device except the Main, one by one, in the reverse of your list order (bottom to top). Click again to re-enable them in your list order (top to bottom).",
                "Désactive tous les périphériques sauf le principal, un par un, dans l'ordre inverse de ta liste (de bas en haut). Reclique pour les réactiver dans l'ordre de ta liste (de haut en bas).",
                "Deaktiviert alle Geräte außer dem Hauptgerät, einzeln, in umgekehrter Listenreihenfolge (von unten nach oben). Erneut klicken, um sie in Listenreihenfolge (von oben nach unten) wiederherzustellen.",
                "Desactiva todos los dispositivos excepto el principal, uno a uno, en el orden inverso de tu lista (de abajo hacia arriba). Vuelve a pulsar para reactivarlos en el orden de tu lista (de arriba hacia abajo).",
                "Disattiva tutte le periferiche tranne la principale, una alla volta, nell'ordine inverso del tuo elenco (dal basso verso l'alto). Clicca di nuovo per riattivarle nell'ordine del tuo elenco (dall'alto verso il basso).",
                "Desativa todos os dispositivos exceto o principal, um a um, na ordem inversa da sua lista (de baixo para cima). Clique de novo para reativá-los na ordem da sua lista (de cima para baixo)." } },
            { "ttOrder", new[]{
                "Reorder the list. This order defines how devices are disabled (reverse) and re-enabled (forward). Saved automatically.",
                "Réordonne la liste. Cet ordre définit comment les périphériques sont désactivés (inverse) et réactivés (dans l'ordre). Mémorisé automatiquement.",
                "Liste neu ordnen. Diese Reihenfolge bestimmt, wie Geräte deaktiviert (umgekehrt) und reaktiviert (vorwärts) werden. Wird automatisch gespeichert.",
                "Reordena la lista. Este orden define cómo se desactivan (inverso) y se reactivan (directo) los dispositivos. Se guarda automáticamente.",
                "Riordina l'elenco. Questo ordine definisce come le periferiche vengono disattivate (inverso) e riattivate (diretto). Salvato automaticamente.",
                "Reordena a lista. Essa ordem define como os dispositivos são desativados (inverso) e reativados (direto). Salvo automaticamente." } },
            { "ttToggleWheel", new[]{
                "Mark the selected device as your Main (kept, never disabled). Click again to unmark. Do this first.",
                "Marque le périphérique sélectionné comme ton principal (gardé, jamais désactivé). Reclique pour retirer. À faire en premier.",
                "Markiert das ausgewählte Gerät als dein Hauptgerät (bleibt, wird nie deaktiviert). Erneut klicken zum Entfernen. Zuerst machen.",
                "Marca el dispositivo seleccionado como tu principal (se mantiene, nunca se desactiva). Vuelve a pulsar para quitar. Hazlo primero.",
                "Segna la periferica selezionata come la tua principale (mantenuta, mai disattivata). Clicca di nuovo per rimuovere. Fallo per primo.",
                "Marca o dispositivo selecionado como seu principal (mantido, nunca desativado). Clique de novo para remover. Faça isso primeiro." } },
            { "ttRefresh", new[]{
                "Re-scan connected devices.", "Re-scanne les périphériques branchés.", "Verbundene Geräte erneut suchen.",
                "Vuelve a escanear los dispositivos conectados.", "Ripeti la scansione delle periferiche collegate.",
                "Verifica novamente os dispositivos conectados." } },
            { "ttJoy", new[]{
                "Open the Windows Game Controllers window to check the order live.",
                "Ouvre la fenêtre Contrôleurs de jeu de Windows pour vérifier l'ordre en direct.",
                "Öffnet das Windows-Fenster Gamecontroller, um die Reihenfolge live zu prüfen.",
                "Abre la ventana Dispositivos de juego de Windows para comprobar el orden en directo.",
                "Apre la finestra Periferiche di gioco di Windows per verificare l'ordine in diretta.",
                "Abre a janela Controles de jogo do Windows para verificar a ordem ao vivo." } },
            { "ttHelp", new[]{
                "Show the full step-by-step guide.", "Affiche le guide complet étape par étape.",
                "Zeigt die vollständige Schritt-für-Schritt-Anleitung.", "Muestra la guía completa paso a paso.",
                "Mostra la guida completa passo passo.", "Mostra o guia completo passo a passo." } },
            { "coffee", new[]{
                "Buy me a coffee", "Offrez-moi un café", "Spendier mir einen Kaffee",
                "Invítame a un café", "Offrimi un caffè", "Pague-me um café" } },
            { "ttDonate", new[]{
                "Opens PayPal to thank the developer. Totally optional, thank you!",
                "Ouvre PayPal pour remercier le développeur. Totalement optionnel, merci !",
                "Öffnet PayPal, um dem Entwickler zu danken. Völlig optional, danke!",
                "Abre PayPal para agradecer al desarrollador. Totalmente opcional, ¡gracias!",
                "Apre PayPal per ringraziare lo sviluppatore. Del tutto facoltativo, grazie!",
                "Abre o PayPal para agradecer ao desenvolvedor. Totalmente opcional, obrigado!" } },
            { "msgDefineTitle", new[]{
                "Define your Main first", "Définis d'abord ton principal", "Zuerst Hauptgerät festlegen",
                "Define primero tu principal", "Definisci prima la principale", "Defina primeiro seu principal" } },
            { "msgDefineBody", new[]{
                "Define your Main device first: select it in the list and click the \"{0}\" button.",
                "Définis d'abord ton périphérique principal : sélectionne-le dans la liste puis clique le bouton \"{0}\".",
                "Lege zuerst dein Hauptgerät fest: in der Liste auswählen und die Schaltfläche \"{0}\" klicken.",
                "Define primero tu dispositivo principal: selecciónalo en la lista y pulsa el botón \"{0}\".",
                "Definisci prima la periferica principale: selezionala nell'elenco e clicca il pulsante \"{0}\".",
                "Defina primeiro seu dispositivo principal: selecione-o na lista e clique no botão \"{0}\"." } },
            { "guideMarkWheel", new[]{
                "Step 1: select your Main device in the list and click \"Mark/unmark selected as Main\".",
                "Étape 1 : sélectionne ton périphérique principal dans la liste puis clique \"Marquer/démarquer comme principal\".",
                "Schritt 1: Wähle dein Hauptgerät in der Liste und klicke \"Auswahl als Hauptgerät markieren\".",
                "Paso 1: selecciona tu dispositivo principal en la lista y pulsa \"Marcar/desmarcar como principal\".",
                "Passo 1: seleziona la periferica principale nell'elenco e clicca \"Segna/rimuovi come principale\".",
                "Passo 1: selecione seu dispositivo principal na lista e clique em \"Marcar/desmarcar como principal\"." } },
            { "btnUp", new[]{ "Move up", "Monter", "Nach oben", "Subir", "Su", "Subir" } },
            { "btnDown", new[]{ "Move down", "Descendre", "Nach unten", "Bajar", "Giù", "Descer" } },
            { "btnJoy", new[]{ "Open joy.cpl", "Ouvrir joy.cpl", "joy.cpl öffnen", "Abrir joy.cpl", "Apri joy.cpl", "Abrir joy.cpl" } },
            { "btnHelp", new[]{ "How to use (Forza)", "Comment utiliser (Forza)", "Anleitung (Forza)", "Cómo usar (Forza)", "Come usare (Forza)", "Como usar (Forza)" } },
            { "helpTitle", new[]{
                "How to use with Forza Horizon", "Comment utiliser avec Forza Horizon", "Anleitung für Forza Horizon",
                "Cómo usar con Forza Horizon", "Come usare con Forza Horizon", "Como usar com Forza Horizon" } },
            { "helpBody", new[]{
                "How to use with Forza Horizon:\r\n\r\n" +
                "STEP 1. Define your Main device: select it in the list and click \"Mark/unmark selected as Main\". It turns blue and is protected (it is never disabled).\r\n\r\n" +
                "STEP 2. Set the order: use \"Move up\" and \"Move down\" to arrange the devices, Main first. This order is saved.\r\n\r\n" +
                "STEP 3. Hide the others: click \"Hide all except the Main\". They are disabled ONE BY ONE from bottom to top (the reverse of the order you set in step 2), leaving the Main alone, so device 1. Tip: click \"Open joy.cpl\" to watch it live.\r\n\r\n" +
                "STEP 4. In the game: launch Forza Horizon and bind your Main device.\r\n\r\n" +
                "<r>Note for some setups: bind your Main device, then SAVE and QUIT the game, relaunch it, and only THEN bring the other devices back (step 5). Do this only if your binding does not hold otherwise.</r>\r\n\r\n" +
                "STEP 5. Bring the others back: click \"Restore all\". They are re-enabled ONE BY ONE from top to bottom (your order), and you can bind everything else WITHOUT leaving the game.\r\n\r\n" +
                "You can also check or uncheck any single device by hand at any time (except the Main).\r\n\r\n" +
                "IMPORTANT: redo this before EVERY game launch. You only repeat the hide and restore steps, NOT the key binding: once done, the game keeps your bindings. That is what replaces physically unplugging. Always keep the same order so your binds stay consistent. A Windows reboot also resets everything.",

                "Comment utiliser avec Forza Horizon :\r\n\r\n" +
                "ÉTAPE 1. Définis ton périphérique principal : sélectionne-le dans la liste puis clique \"Marquer/démarquer comme principal\". Il passe en bleu et devient protégé (il n'est jamais désactivé).\r\n\r\n" +
                "ÉTAPE 2. Définis l'ordre : avec \"Monter\" et \"Descendre\", range les périphériques, le principal en premier. Cet ordre est mémorisé.\r\n\r\n" +
                "ÉTAPE 3. Cache les autres : clique \"Cacher tout sauf le principal\". Ils sont désactivés UN PAR UN de bas en haut (l'inverse de l'ordre que tu as défini à l'étape 2), le principal reste seul, donc device 1. Astuce : clique \"Ouvrir joy.cpl\" pour voir en direct.\r\n\r\n" +
                "ÉTAPE 4. Dans le jeu : lance Forza Horizon et bind ton périphérique principal.\r\n\r\n" +
                "<r>Note pour certaines configurations : bind ton principal, puis SAUVEGARDE et QUITTE le jeu, relance-le, et seulement APRÈS réactive les autres périphériques (étape 5). À faire uniquement si tes binds ne tiennent pas autrement.</r>\r\n\r\n" +
                "ÉTAPE 5. Ramène les autres : clique \"Tout restaurer\". Ils sont réactivés UN PAR UN de haut en bas (ton ordre), et tu peux tout binder SANS quitter le jeu.\r\n\r\n" +
                "Tu peux aussi cocher ou décocher chaque périphérique à la main à tout moment (sauf le principal).\r\n\r\n" +
                "IMPORTANT : refais cette manip avant CHAQUE démarrage du jeu. Tu refais seulement le masquage et la restauration, PAS le re-binding des touches : une fois faits, tes binds sont gardés par le jeu. C'est ce qui remplace le débranchement physique. Garde toujours le même ordre pour que tes binds restent cohérents. Un redémarrage Windows remet aussi tout normal.",

                "So benutzt du es mit Forza Horizon:\r\n\r\n" +
                "SCHRITT 1. Hauptgerät festlegen: in der Liste auswählen und \"Auswahl als Hauptgerät markieren\" klicken. Es wird blau und ist geschützt (es wird nie deaktiviert).\r\n\r\n" +
                "SCHRITT 2. Reihenfolge festlegen: mit \"Nach oben\" und \"Nach unten\" die Geräte ordnen, Hauptgerät zuerst. Diese Reihenfolge wird gespeichert.\r\n\r\n" +
                "SCHRITT 3. Andere ausblenden: \"Alles außer Hauptgerät ausblenden\" klicken. Sie werden EINZELN von unten nach oben deaktiviert (umgekehrt zu der in Schritt 2 festgelegten Reihenfolge), das Hauptgerät bleibt allein, also Gerät 1. Tipp: \"joy.cpl öffnen\" zum Mitschauen.\r\n\r\n" +
                "SCHRITT 4. Im Spiel: Forza Horizon starten und das Hauptgerät belegen.\r\n\r\n" +
                "<r>Hinweis für manche Setups: Hauptgerät belegen, dann das Spiel SPEICHERN und BEENDEN, neu starten, und ERST DANN die anderen Geräte wiederherstellen (Schritt 5). Nur nötig, wenn deine Belegung sonst nicht hält.</r>\r\n\r\n" +
                "SCHRITT 5. Andere zurückholen: \"Alle wiederherstellen\" klicken. Sie werden EINZELN von oben nach unten (deine Reihenfolge) reaktiviert, und du kannst alles belegen, OHNE das Spiel zu verlassen.\r\n\r\n" +
                "Du kannst jedes Gerät auch jederzeit von Hand an- oder abwählen (außer dem Hauptgerät).\r\n\r\n" +
                "WICHTIG: Wiederhole das vor JEDEM Spielstart. Du wiederholst nur das Ausblenden und Wiederherstellen, NICHT die Tastenbelegung: einmal erledigt, behält das Spiel deine Belegung. Das ersetzt das physische Abstecken. Behalte immer dieselbe Reihenfolge, damit deine Belegungen konsistent bleiben. Ein Neustart setzt ebenfalls alles zurück.",

                "Cómo usarlo con Forza Horizon:\r\n\r\n" +
                "PASO 1. Define tu dispositivo principal: selecciónalo en la lista y pulsa \"Marcar/desmarcar como principal\". Se pone azul y queda protegido (nunca se desactiva).\r\n\r\n" +
                "PASO 2. Define el orden: con \"Subir\" y \"Bajar\", ordena los dispositivos, el principal primero. Este orden se guarda.\r\n\r\n" +
                "PASO 3. Oculta los demás: pulsa \"Ocultar todo excepto el principal\". Se desactivan UNO A UNO de abajo hacia arriba (el inverso del orden que definiste en el paso 2), el principal queda solo, es decir dispositivo 1. Consejo: pulsa \"Abrir joy.cpl\" para verlo en directo.\r\n\r\n" +
                "PASO 4. En el juego: inicia Forza Horizon y configura tu dispositivo principal.\r\n\r\n" +
                "<r>Nota para algunas configuraciones: configura el principal, luego GUARDA y SAL del juego, vuelve a abrirlo, y SOLO ENTONCES restaura los demás dispositivos (paso 5). Hazlo solo si tus asignaciones no se mantienen de otro modo.</r>\r\n\r\n" +
                "PASO 5. Recupera los demás: pulsa \"Restaurar todo\". Se reactivan UNO A UNO de arriba hacia abajo (tu orden), y puedes configurarlos todos SIN salir del juego.\r\n\r\n" +
                "También puedes marcar o desmarcar cada dispositivo a mano en cualquier momento (excepto el principal).\r\n\r\n" +
                "IMPORTANTE: repite esto antes de CADA inicio del juego. Solo repites ocultar y restaurar, NO la reasignación de botones: una vez hecha, el juego conserva tus asignaciones. Eso es lo que evita desconectar físicamente. Mantén siempre el mismo orden para que tus asignaciones se mantengan. Reiniciar Windows también restablece todo.",

                "Come usarlo con Forza Horizon:\r\n\r\n" +
                "PASSO 1. Definisci la periferica principale: selezionala nell'elenco e clicca \"Segna/rimuovi come principale\". Diventa blu ed è protetta (non viene mai disattivata).\r\n\r\n" +
                "PASSO 2. Definisci l'ordine: con \"Su\" e \"Giù\" ordina le periferiche, la principale per prima. Questo ordine viene salvato.\r\n\r\n" +
                "PASSO 3. Nascondi le altre: clicca \"Nascondi tutto tranne la principale\". Vengono disattivate UNA ALLA VOLTA dal basso verso l'alto (l'inverso dell'ordine definito al passo 2), la principale resta da sola, quindi dispositivo 1. Suggerimento: clicca \"Apri joy.cpl\" per vederlo in diretta.\r\n\r\n" +
                "PASSO 4. Nel gioco: avvia Forza Horizon e configura la periferica principale.\r\n\r\n" +
                "<r>Nota per alcune configurazioni: configura la principale, poi SALVA ed ESCI dal gioco, riavvialo, e SOLO ALLORA ripristina le altre periferiche (passo 5). Fallo solo se i tuoi binding non restano altrimenti.</r>\r\n\r\n" +
                "PASSO 5. Riporta le altre: clicca \"Ripristina tutto\". Vengono riattivate UNA ALLA VOLTA dall'alto verso il basso (il tuo ordine), e puoi configurarle tutte SENZA uscire dal gioco.\r\n\r\n" +
                "Puoi anche selezionare o deselezionare ogni periferica a mano in qualsiasi momento (tranne la principale).\r\n\r\n" +
                "IMPORTANTE: ripeti questa procedura prima di OGNI avvio del gioco. Ripeti solo nascondere e ripristinare, NON la riassegnazione dei tasti: una volta fatta, il gioco conserva i tuoi binding. È ciò che evita di scollegare fisicamente. Mantieni sempre lo stesso ordine affinché i tuoi binding restino coerenti. Anche un riavvio di Windows ripristina tutto.",

                "Como usar com Forza Horizon:\r\n\r\n" +
                "PASSO 1. Defina seu dispositivo principal: selecione-o na lista e clique em \"Marcar/desmarcar como principal\". Ele fica azul e protegido (nunca é desativado).\r\n\r\n" +
                "PASSO 2. Defina a ordem: com \"Subir\" e \"Descer\", organize os dispositivos, o principal primeiro. Essa ordem é salva.\r\n\r\n" +
                "PASSO 3. Oculte os outros: clique em \"Ocultar tudo exceto o principal\". Eles são desativados UM A UM de baixo para cima (o inverso da ordem definida no passo 2), o principal fica sozinho, ou seja dispositivo 1. Dica: clique em \"Abrir joy.cpl\" para ver ao vivo.\r\n\r\n" +
                "PASSO 4. No jogo: inicie o Forza Horizon e configure seu dispositivo principal.\r\n\r\n" +
                "<r>Nota para algumas configurações: configure o principal, depois SALVE e SAIA do jogo, reinicie-o, e SÓ ENTÃO restaure os outros dispositivos (passo 5). Faça isso só se seus binds não permanecerem de outra forma.</r>\r\n\r\n" +
                "PASSO 5. Traga os outros de volta: clique em \"Restaurar tudo\". Eles são reativados UM A UM de cima para baixo (sua ordem), e você pode configurar tudo SEM sair do jogo.\r\n\r\n" +
                "Você também pode marcar ou desmarcar cada dispositivo manualmente a qualquer momento (exceto o principal).\r\n\r\n" +
                "IMPORTANTE: refaça isto antes de CADA início do jogo. Você só repete ocultar e restaurar, NÃO o rebind das teclas: uma vez feito, o jogo guarda seus binds. É o que evita desconectar fisicamente. Mantenha sempre a mesma ordem para que seus binds permaneçam consistentes. Reiniciar o Windows também redefine tudo." } },
        };

        public static string T(string key)
        {
            string[] v;
            if (M.TryGetValue(key, out v) && Lang >= 0 && Lang < v.Length) return v[Lang];
            return key;
        }
    }

    public class Controller
    {
        public string Name { get; set; }
        public string VidPid { get; set; }
        public string InstanceId { get; set; }
        public bool Active { get; set; }
        public bool IsWheel { get; set; }   // from PowerShell = suggestion (known brand)
    }

    // === Saved choice (language + Main devices) ===
    internal static class Config
    {
        private static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeviceOrder"); } }
        private static string File_ { get { return Path.Combine(Dir, "config.txt"); } }
        public static bool Existed;
        public static bool FirstRun;

        public static int LangIndex = -1;                 // -1 = not set
        public static readonly HashSet<string> Wheels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // User-defined re-enable order (list of VID&PID).
        public static readonly List<string> Order = new List<string>();

        public static void Load()
        {
            try
            {
                if (!File.Exists(File_)) { Existed = false; FirstRun = true; return; }
                Existed = true;
                foreach (var line in File.ReadAllLines(File_))
                {
                    var s = line.Trim();
                    if (s.StartsWith("lang="))
                    {
                        var code = s.Substring(5).Trim();
                        for (int i = 0; i < L.Codes.Length; i++) if (L.Codes[i] == code) LangIndex = i;
                    }
                    else if (s.StartsWith("wheel="))
                    {
                        foreach (var vp in s.Substring(6).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            Wheels.Add(vp.Trim().ToUpper());
                    }
                    else if (s.StartsWith("order="))
                    {
                        foreach (var vp in s.Substring(6).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            Order.Add(vp.Trim().ToUpper());
                    }
                }
            }
            catch { Existed = false; }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("lang=" + L.Codes[L.Lang]);
                sb.AppendLine("wheel=" + string.Join(";", new List<string>(Wheels).ToArray()));
                sb.AppendLine("order=" + string.Join(";", Order.ToArray()));
                File.WriteAllText(File_, sb.ToString());
                Existed = true;
            }
            catch { /* non bloquant */ }
        }
    }

    // === PnP layer (via Windows PowerShell cmdlets) ===
    internal static class Pnp
    {
        private const string DetectScript = @"
$ErrorActionPreference='SilentlyContinue'
$all = Get-PnpDevice -PresentOnly

# 1) ABSOLUTE exclusion: any VID&PID that carries a keyboard/mouse
$kbm=@{}
foreach($d in ($all | Where-Object { $_.Class -eq 'Keyboard' -or $_.Class -eq 'Mouse' })){
  if($d.InstanceId -match '(VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4})'){ $kbm[$Matches[1].ToUpper()]=$true }
}

# 2) Friendly names (Joystick OEM registry), DISPLAY ONLY
$oem=@{}
foreach($p in @(
  'HKLM:\SYSTEM\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM',
  'HKCU:\System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM')){
  if(Test-Path $p){ foreach($k in (Get-ChildItem $p)){
    $vp=$k.PSChildName
    if($vp -match '^VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}$'){
      $nm=(Get-ItemProperty $k.PSPath -Name OEMName).OEMName
      if($nm){ $oem[$vp.ToUpper()]=$nm }
    } } }
}

# Known wheel/base VIDs, SUGGESTION only (the user can always override).
# MOZA 346E, Fanatec 0EB7, Thrustmaster 044F, Simucube/Granite 16D0, Asetek 2433.
$wheelVids='VID_346E|VID_0EB7|VID_044F|VID_16D0|VID_2433'

# 3) Candidates = HID 'game controller' OR known wheel ; dedupe ; excluding keyboard/mouse
$seen=@{}; $res=@()
foreach($d in ($all | Where-Object {
      $_.Class -eq 'HIDClass' -and (
        $_.FriendlyName -match 'game.?controller|jeu' -or $_.InstanceId -match $wheelVids) })){
  if($d.InstanceId -notmatch '(VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4})'){ continue }
  $vp=$Matches[1].ToUpper()
  if($seen[$vp] -or $kbm[$vp]){ continue }
  $seen[$vp]=$true
  $usb=$all | Where-Object { $_.InstanceId -match ('^USB\\'+[regex]::Escape($vp)+'\\[^\\]+$') } | Select-Object -First 1
  if(-not $usb){ continue }
  $nm=$oem[$vp]; if(-not $nm){ $nm=$d.FriendlyName }
  $res+=[PSCustomObject]@{
    Name=$nm; VidPid=$vp; InstanceId=$usb.InstanceId
    Active=($usb.Status -eq 'OK'); IsWheel=[bool]($vp -match $wheelVids)
  }
}
$res=$res | Sort-Object @{Expression={ -not $_.IsWheel }}, Name
'['+(($res | ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 3 }) -join ',')+']'
";

        private static string RunPowerShell(string script, out string error)
        {
            error = null;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrWhiteSpace(err)) error = err.Trim();
                return outp;
            }
        }

        public static List<Controller> Detect()
        {
            string err;
            string json = RunPowerShell(DetectScript, out err);
            var list = new List<Controller>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                var arr = new JavaScriptSerializer().Deserialize<List<Controller>>(json.Trim());
                if (arr != null) list = arr;
            }
            catch { }
            return list;
        }

        public static void Disable(string instanceId) { SetState(instanceId, false); }
        public static void Enable(string instanceId) { SetState(instanceId, true); }

        private static void SetState(string instanceId, bool enable)
        {
            string cmdlet = enable ? "Enable-PnpDevice" : "Disable-PnpDevice";
            string idB64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(instanceId));
            string script =
                "$id=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + idB64 + "'));" +
                "try{ " + cmdlet + " -InstanceId $id -Confirm:$false -ErrorAction Stop; 'OK' }" +
                "catch{ 'ERR:'+$_.Exception.Message }";
            string err;
            string trimmed = (RunPowerShell(script, out err) ?? "").Trim();
            if (trimmed.StartsWith("ERR:")) throw new InvalidOperationException(trimmed.Substring(4).Trim());
            if (!string.IsNullOrEmpty(err) && !trimmed.StartsWith("OK")) throw new InvalidOperationException(err);
        }
    }

    // Custom-drawn "PayPal" style button: official rounded yellow background + two-tone wordmark + caption.
    public class PaypalButton : Button
    {
        public string Caption = "";
        private bool _hover;
        private static readonly Color Yellow = Color.FromArgb(255, 196, 57);
        private static readonly Color YellowHover = Color.FromArgb(255, 209, 102);
        private static readonly Color PayDark = Color.FromArgb(0, 48, 135);
        private static readonly Color PalLight = Color.FromArgb(0, 156, 222);

        public PaypalButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Text = "";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        private static Font WordFont() { return new Font("Arial", 12f, FontStyle.Bold | FontStyle.Italic); }
        private static Font CapFont() { return new Font("Segoe UI", 8.5f, FontStyle.Regular); }

        // Fit the button width to its content (logo + caption).
        public void Recalc()
        {
            using (var g = CreateGraphics())
            using (var wf = WordFont())
            using (var cf = CapFont())
            {
                float w = 14 + g.MeasureString("Pay", wf).Width + g.MeasureString("Pal", wf).Width;
                if (!string.IsNullOrEmpty(Caption)) w += 8 + g.MeasureString(Caption, cf).Width;
                w += 14;
                Width = (int)Math.Ceiling(w);
                Height = 30;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // Fill first with the parent background: otherwise the area outside the rounded corners
            // shows square edges behind the yellow button. Here the corners blend in = clean.
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(rect, 7))
            using (var b = new SolidBrush(_hover ? YellowHover : Yellow))
                g.FillPath(b, path);

            using (var wf = WordFont())
            using (var cf = CapFont())
            using (var payB = new SolidBrush(PayDark))
            using (var palB = new SolidBrush(PalLight))
            {
                float payW = g.MeasureString("Pay", wf).Width;
                float wordH = g.MeasureString("Pay", wf).Height;
                float x = 12;
                float y = (Height - wordH) / 2f;
                g.DrawString("Pay", wf, payB, x, y);
                g.DrawString("Pal", wf, palB, x + payW - 5, y);
                if (!string.IsNullOrEmpty(Caption))
                {
                    float palW = g.MeasureString("Pal", wf).Width;
                    float cx = x + payW - 5 + palW + 2;
                    float capH = g.MeasureString(Caption, cf).Height;
                    g.DrawString(Caption, cf, payB, cx, (Height - capH) / 2f);
                }
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Displays an image resized with high quality smoothing (no pixelation), transparent
    // background. An SVG can also be supplied; it is rasterized and drawn here at the wanted size.
    public class ImageBox : Control
    {
        private Image _img;
        private string _svg;
        private Bitmap _svgCache;
        private int _svgCacheH = -1;
        public Image Img { get { return _img; } set { _img = value; Invalidate(); } }
        // When an SVG is provided, it is RASTERIZED at the control's exact size, re-rendered on
        // every resize (so on every DPI). The vector is therefore drawn at the real final
        // resolution => crisp at 100%, no pixelation of a precomputed bitmap.
        public string Svg { set { _svg = value; InvalidateSvgCache(); Invalidate(); } }

        public ImageBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        private void InvalidateSvgCache()
        {
            if (_svgCache != null) { _svgCache.Dispose(); _svgCache = null; }
            _svgCacheH = -1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_svg != null) { InvalidateSvgCache(); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (_svg != null)
            {
                int h = Height;
                if (h <= 0) return;
                if (_svgCache == null || _svgCacheH != h)
                {
                    if (_svgCache != null) _svgCache.Dispose();
                    int ss = Math.Min(h * 3, 600);          // 3x supersampling for very smooth antialiasing
                    _svgCache = SvgImage.Render(_svg, ss);   // high-resolution render, downscaled to the exact pixel size
                    _svgCacheH = h;
                }
                e.Graphics.DrawImage(_svgCache, new Rectangle(0, 0, Width, Height));
                return;
            }
            if (_img == null) return;
            e.Graphics.DrawImage(_img, new Rectangle(0, 0, Width, Height));
        }
    }

    // Palette derived from the DeviceOrder logo (background #06090D, accent #409DFB, white text).
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(6, 9, 13);
        public static readonly Color Panel = Color.FromArgb(13, 19, 28);
        public static readonly Color Accent = Color.FromArgb(64, 157, 251);
        public static readonly Color AccentHover = Color.FromArgb(100, 178, 255);
        public static readonly Color AccentDark = Color.FromArgb(30, 102, 200);
        public static readonly Color Text = Color.White;
        public static readonly Color SubText = Color.FromArgb(165, 178, 192);
        public static readonly Color WheelRow = Color.FromArgb(20, 52, 84);   // blue highlight for the main device
        public static readonly Color HiddenText = Color.FromArgb(110, 122, 134);
        public static readonly Color Line = Color.FromArgb(28, 38, 50);
    }

    // Small SVG renderer (commands M L H V C S Z, absolute/relative; no arcs).
    // Enough to rasterize the DeviceOrder logo, crisp at the wanted size.
    internal static class SvgImage
    {
        public static Bitmap Render(string svg, int targetHeight)
        {
            float vw = 0, vh = 0;
            var vb = Regex.Match(svg, "viewBox=\"([^\"]+)\"");
            if (vb.Success)
            {
                var p = vb.Groups[1].Value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 4) { vw = F(p[2]); vh = F(p[3]); }
            }
            if (vw <= 0 || vh <= 0) { vw = 100; vh = 100; }
            float scale = targetHeight / vh;
            int tw = Math.Max(1, (int)Math.Ceiling(vw * scale));
            var bmp = new Bitmap(tw, targetHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.ScaleTransform(scale, scale);
                foreach (Match m in Regex.Matches(svg, "<(path|rect)\\b([^>]*?)/?>"))
                {
                    string tag = m.Groups[1].Value, attrs = m.Groups[2].Value;
                    Color fill = ParseColor(Attr(attrs, "fill"));
                    GraphicsPath gp = (tag == "rect") ? RectPath(attrs) : PathData(Attr(attrs, "d"));
                    if (gp == null) continue;
                    using (gp)
                    using (var b = new SolidBrush(fill))
                        g.FillPath(b, gp);
                }
            }
            return bmp;
        }

        private static float F(string s) { float v; float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v); return v; }
        private static string Attr(string attrs, string name)
        {
            var m = Regex.Match(attrs, name + "=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static Color ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.Black;
            hex = hex.Trim();
            if (!hex.StartsWith("#")) return Color.Black;
            hex = hex.Substring(1);
            if (hex.Length == 3)
                hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
            if (hex.Length != 6) return Color.Black;
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return Color.FromArgb(r, g, b);
        }

        private static GraphicsPath RectPath(string attrs)
        {
            float x = F(Attr(attrs, "x")), y = F(Attr(attrs, "y"));
            float w = F(Attr(attrs, "width")), h = F(Attr(attrs, "height"));
            float rx = F(Attr(attrs, "rx"));
            var gp = new GraphicsPath();
            if (rx <= 0) { gp.AddRectangle(new RectangleF(x, y, w, h)); return gp; }
            float d = rx * 2;
            gp.AddArc(x, y, d, d, 180, 90);
            gp.AddArc(x + w - d, y, d, d, 270, 90);
            gp.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            gp.AddArc(x, y + h - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        // Tokenize and build a GraphicsPath from the "d" attribute.
        private static GraphicsPath PathData(string d)
        {
            if (string.IsNullOrEmpty(d)) return null;
            var toks = Tokenize(d);
            var gp = new GraphicsPath { FillMode = FillMode.Winding };
            int p = 0; char cmd = ' ', prev = ' ';
            float cx = 0, cy = 0, sx = 0, sy = 0, lcx = 0, lcy = 0; bool open = false;
            while (p < toks.Count)
            {
                if (toks[p] is char) { cmd = (char)toks[p]; p++; }
                char c = cmd;
                switch (c)
                {
                    case 'M': case 'm':
                        {
                            float x = N(toks, ref p), y = N(toks, ref p);
                            if (c == 'm') { x += cx; y += cy; }
                            cx = x; cy = y; sx = x; sy = y;
                            gp.StartFigure(); open = true;
                            cmd = (c == 'm') ? 'l' : 'L';
                            break;
                        }
                    case 'L': case 'l':
                        {
                            float x = N(toks, ref p), y = N(toks, ref p);
                            if (c == 'l') { x += cx; y += cy; }
                            gp.AddLine(cx, cy, x, y); cx = x; cy = y; break;
                        }
                    case 'H': case 'h':
                        {
                            float x = N(toks, ref p); if (c == 'h') x += cx;
                            gp.AddLine(cx, cy, x, cy); cx = x; break;
                        }
                    case 'V': case 'v':
                        {
                            float y = N(toks, ref p); if (c == 'v') y += cy;
                            gp.AddLine(cx, cy, cx, y); cy = y; break;
                        }
                    case 'C': case 'c':
                        {
                            float x1 = N(toks, ref p), y1 = N(toks, ref p), x2 = N(toks, ref p),
                                  y2 = N(toks, ref p), x = N(toks, ref p), y = N(toks, ref p);
                            if (c == 'c') { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                            gp.AddBezier(cx, cy, x1, y1, x2, y2, x, y);
                            lcx = x2; lcy = y2; cx = x; cy = y; break;
                        }
                    case 'S': case 's':
                        {
                            float x2 = N(toks, ref p), y2 = N(toks, ref p), x = N(toks, ref p), y = N(toks, ref p);
                            if (c == 's') { x2 += cx; y2 += cy; x += cx; y += cy; }
                            float x1, y1;
                            if (prev == 'C' || prev == 'c' || prev == 'S' || prev == 's') { x1 = 2 * cx - lcx; y1 = 2 * cy - lcy; }
                            else { x1 = cx; y1 = cy; }
                            gp.AddBezier(cx, cy, x1, y1, x2, y2, x, y);
                            lcx = x2; lcy = y2; cx = x; cy = y; break;
                        }
                    case 'Z': case 'z':
                        if (open) { gp.CloseFigure(); open = false; }
                        cx = sx; cy = sy; break;
                    default:
                        p++; break;
                }
                prev = c;
            }
            return gp;
        }

        private static float N(List<object> toks, ref int p)
        {
            while (p < toks.Count && toks[p] is char) p++;          // safety
            if (p >= toks.Count) return 0;
            return (float)(double)toks[p++];
        }

        private static List<object> Tokenize(string d)
        {
            var toks = new List<object>(); int i = 0, n = d.Length;
            while (i < n)
            {
                char ch = d[i];
                if (char.IsLetter(ch)) { toks.Add(ch); i++; }
                else if (ch == ',' || char.IsWhiteSpace(ch)) { i++; }
                else
                {
                    int start = i;
                    if (d[i] == '+' || d[i] == '-') i++;
                    while (i < n && char.IsDigit(d[i])) i++;
                    if (i < n && d[i] == '.') { i++; while (i < n && char.IsDigit(d[i])) i++; }
                    if (i < n && (d[i] == 'e' || d[i] == 'E'))
                    {
                        i++; if (i < n && (d[i] == '+' || d[i] == '-')) i++;
                        while (i < n && char.IsDigit(d[i])) i++;
                    }
                    if (i == start) { i++; continue; }
                    double val; double.TryParse(d.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out val);
                    toks.Add(val);
                }
            }
            return toks;
        }
    }

    // Custom-drawn quick guide: bold title, then each step on its own line with "N)" in bold
    // (accent blue) and the step text in regular gray. Handles word wrapping.
    public class QuickGuide : Control
    {
        public string Title = "";
        public string[] Steps = new string[0];

        public QuickGuide()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
        }

        public void Set(string title, string[] steps)
        {
            Title = title ?? "";
            Steps = steps ?? new string[0];
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bb = new SolidBrush(Theme.Bg)) g.FillRectangle(bb, ClientRectangle);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var bold = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (var reg = new Font("Segoe UI", 9.5f, FontStyle.Regular))
            {
                int x = 18, y = 6, avail = Width - x - 14;
                int lh = TextRenderer.MeasureText(g, "Ag", reg).Height;
                var noPrefix = TextFormatFlags.NoPrefix;
                var wrap = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

                TextRenderer.DrawText(g, Title, bold, new Rectangle(x, y, avail, lh + 2), Theme.Text, noPrefix);
                y += lh + 7;

                for (int i = 0; i < Steps.Length; i++)
                {
                    string num = (i + 1) + ")  ";
                    Size ns = TextRenderer.MeasureText(g, num, bold, new Size(int.MaxValue, lh), noPrefix);
                    TextRenderer.DrawText(g, num, bold, new Rectangle(x, y, ns.Width, lh + 2), Theme.Accent, noPrefix);
                    int tx = x + ns.Width, tw = avail - ns.Width;
                    Size needed = TextRenderer.MeasureText(g, Steps[i], reg, new Size(tw, int.MaxValue), wrap);
                    TextRenderer.DrawText(g, Steps[i], reg, new Rectangle(tx, y, tw, needed.Height + 2), Theme.SubText, wrap);
                    y += Math.Max(lh, needed.Height) + 5;
                }
            }
        }
    }

    // ListView that NEVER shows a horizontal bar (everything fits the width). We remove the
    // WS_HSCROLL style on every non-client calc, so the phantom bar can no longer appear.
    // The VERTICAL bar stays available (useful with many devices).
    public class NoHScrollListView : ListView
    {
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        private const int GWL_STYLE = -16;
        private const int WS_HSCROLL = 0x00100000;
        private const int WM_NCCALCSIZE = 0x0083;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCCALCSIZE)
            {
                int style = GetWindowLong(Handle, GWL_STYLE);
                if ((style & WS_HSCROLL) != 0)
                    SetWindowLong(Handle, GWL_STYLE, style & ~WS_HSCROLL);
            }
            base.WndProc(ref m);
        }
    }

    public class MainForm : Form
    {
        private ListView _list;
        private Button _btnToggle, _btnRefresh, _btnToggleWheel, _btnJoy, _btnHelp, _btnUp, _btnDown;
        private bool _isolated;   // false = normal state (action: hide), true = isolated (action: restore)
        private ToolTip _tips;
        private PaypalButton _donate;
        // Developer PayPal link. Replace the value below with your real PayPal.me link.
        private const string DonateUrl = "https://www.paypal.com/paypalme/SinepticStudio";
        private Label _status, _langLabel;
        private QuickGuide _header;
        private FlowLayoutPanel _btnPanel;
        private ComboBox _langBox;
        private ImageBox _logoBox;
        private Label _btnMin, _btnClose;
        private Panel _topBar;
        private NotifyIcon _tray;
        private bool _suppress;
        private List<Controller> _controllers = new List<Controller>();
        private readonly HashSet<string> _weDisabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public MainForm()
        {
            Width = 1220; Height = 660;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            MinimumSize = new Size(1120, 580);
            FormBorderStyle = FormBorderStyle.None;     // custom title bar (dark + blue buttons)
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Icon = LoadAppIcon();

            _tips = new ToolTip { AutoPopDelay = 30000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };

            // Top banner: logo (SVG) on the left, close/minimize + language/PayPal on the right
            _topBar = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.Bg };
            _logoBox = new ImageBox { Left = 18, Top = 18 };
            try
            {
                // The logo is an SVG (vector): it is rasterized at the EXACT display size,
                // re-rendered on every DPI (see ImageBox). Crisp at 100%, no pixelation.
                string logoSvg = LoadText("logo.svg");
                int boxH = 42;   // logo height (reduced ~15% more)
                using (var probe = SvgImage.Render(logoSvg, boxH))
                    _logoBox.Size = new Size(probe.Width, boxH);
                _logoBox.Svg = logoSvg;
            }
            catch { }
            _logoBox.MouseDown += TitleDrag;
            _topBar.Controls.Add(_logoBox);

            _btnClose = MakeCaptionButton("✕", true);
            _btnMin = MakeCaptionButton("–", false);
            _btnClose.Click += (s, e) => Close();
            _btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            _topBar.Controls.Add(_btnClose);
            _topBar.Controls.Add(_btnMin);

            _langLabel = new Label { AutoSize = true, ForeColor = Theme.SubText };
            _langBox = new ComboBox
            {
                Width = 150, DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, BackColor = Theme.Panel, ForeColor = Theme.Text
            };
            _langBox.Items.AddRange(L.Names);
            _langBox.SelectedIndexChanged += (s, e) =>
            {
                if (_langBox.SelectedIndex < 0 || _langBox.SelectedIndex == L.Lang) return;
                L.Lang = _langBox.SelectedIndex;
                Config.Save();
                ApplyTexts();
                RenderList();
                FitWindowToButtons();
            };
            _donate = new PaypalButton();
            _donate.Click += (s, e) =>
            {
                if (DonateUrl.Contains("CHANGE_ME")) return;
                try { Process.Start(new ProcessStartInfo(DonateUrl) { UseShellExecute = true }); }
                catch { }
            };
            _topBar.Controls.Add(_langLabel);
            _topBar.Controls.Add(_langBox);
            _topBar.Controls.Add(_donate);
            _topBar.MouseDown += TitleDrag;
            _topBar.MouseDoubleClick += (s, e) => ToggleMax();
            _topBar.Resize += (s, e) => LayoutTopBar();

            // Quick guide (bold title + numbers, steps on separate lines)
            _header = new QuickGuide { Dock = DockStyle.Top, Height = 104 };

            // Device list (dark)
            _list = new NoHScrollListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
                GridLines = false, HideSelection = false, OwnerDraw = true, BorderStyle = BorderStyle.None,
                BackColor = Theme.Bg, ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 11f)   // larger text for controllers / VID&PID / state
            };
            _list.Columns.Add("", 430);
            _list.Columns.Add("", 185);
            _list.Columns.Add("", 235);
            _list.Columns.Add("", 200);   // filler column (prevents a white sliver on the right)
            _list.ItemCheck += OnItemCheck;
            _list.DrawColumnHeader += ListDrawHeader;
            _list.DrawItem += (s, e) => e.DrawDefault = true;
            _list.DrawSubItem += (s, e) => e.DrawDefault = true;
            _list.Resize += (s, e) => FitLastColumn();

            // Buttons (blue)
            _btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 102, Padding = new Padding(12, 10, 12, 10), FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Theme.Bg };
            _btnToggle = new Button();
            _btnToggleWheel = new Button();
            _btnRefresh = new Button();
            _btnJoy = new Button();
            _btnHelp = new Button();
            _btnUp = new Button();
            _btnDown = new Button();
            foreach (var b in new[] { _btnToggle, _btnUp, _btnDown, _btnToggleWheel, _btnRefresh, _btnJoy, _btnHelp })
                StyleButton(b);
            _btnToggle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _btnToggle.Click += (s, e) => OnTogglePower();
            _btnToggleWheel.Click += (s, e) => ToggleWheelSelection();
            _btnRefresh.Click += (s, e) => Refresh_();
            _btnJoy.Click += (s, e) => OpenJoyCpl();
            _btnHelp.Click += (s, e) => ShowHelp();
            _btnUp.Click += (s, e) => MoveSelected(-1);
            _btnDown.Click += (s, e) => MoveSelected(+1);
            _btnPanel.Controls.Add(_btnToggle);
            _btnPanel.Controls.Add(_btnUp);
            _btnPanel.Controls.Add(_btnDown);
            _btnPanel.Controls.Add(_btnToggleWheel);
            _btnPanel.Controls.Add(_btnRefresh);
            _btnPanel.Controls.Add(_btnJoy);
            _btnPanel.Controls.Add(_btnHelp);

            _status = new Label { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0), BackColor = Theme.Panel, ForeColor = Theme.Text };

            Controls.Add(_list);
            Controls.Add(_btnPanel);
            Controls.Add(_status);
            Controls.Add(_header);
            Controls.Add(_topBar);

            // System tray icon
            if (Icon != null)
            {
                _tray = new NotifyIcon { Icon = Icon, Text = "DeviceOrder", Visible = true };
                _tray.DoubleClick += (s, e) => RestoreFromTray();
                var menu = new ContextMenuStrip();
                menu.Items.Add("DeviceOrder", null, (s, e) => RestoreFromTray());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Quit", null, (s, e) => Close());
                _tray.ContextMenuStrip = menu;
            }

            _langBox.SelectedIndex = L.Lang;
            ApplyTexts();
            LayoutTopBar();
            FitLastColumn();
            Load += (s, e) =>
            {
                Refresh_();
                FitWindowToButtons();
                if (Config.FirstRun) { ChooseLanguageFirstRun(); ShowHelp(); }
            };
            FormClosing += OnClosingSafetyNet;
        }

        // UI helpers (dark theme, custom title bar)

        private static Icon LoadAppIcon()
        {
            try
            {
                var a = Assembly.GetExecutingAssembly();
                using (var st = a.GetManifestResourceStream("app.ico"))
                    if (st != null) return new Icon(st);
            }
            catch { }
            return null;
        }

        private static string LoadText(string resName)
        {
            var a = Assembly.GetExecutingAssembly();
            using (var s = a.GetManifestResourceStream(resName))
            {
                if (s == null) return "";
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        private Label MakeCaptionButton(string glyph, bool isClose)
        {
            var l = new Label
            {
                Text = glyph, Size = new Size(46, 34), TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Accent, BackColor = Theme.Bg, Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11f, FontStyle.Regular)
            };
            l.MouseEnter += (s, e) => { l.BackColor = isClose ? Color.FromArgb(196, 43, 64) : Theme.Panel; l.ForeColor = isClose ? Color.White : Theme.AccentHover; };
            l.MouseLeave += (s, e) => { l.BackColor = Theme.Bg; l.ForeColor = Theme.Accent; };
            return l;
        }

        private void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Theme.Accent;
            b.ForeColor = Color.White;
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            b.AutoSize = true;
            b.Height = 34;
            b.Margin = new Padding(4);
            b.Padding = new Padding(12, 6, 12, 6);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            b.FlatAppearance.MouseDownBackColor = Theme.AccentDark;
        }

        // Splits the localized "header" string (title + "1) ... 2) ... 3) ...") into parts
        // to display them as a bold title + numbered steps.
        private void BuildGuide()
        {
            if (_header == null) return;
            string s = L.T("header");
            int i1 = s.IndexOf("1)"), i2 = s.IndexOf("2)"), i3 = s.IndexOf("3)");
            if (i1 >= 0 && i2 > i1 && i3 > i2)
            {
                string title = s.Substring(0, i1).Trim();
                string s1 = s.Substring(i1 + 2, i2 - i1 - 2).Trim();
                string s2 = s.Substring(i2 + 2, i3 - i2 - 2).Trim();
                string s3 = s.Substring(i3 + 2).Trim();
                _header.Set(title, new[] { s1, s2, s3 });
            }
            else _header.Set(s, new string[0]);
        }

        // Adjusts the window width to fit the button row (removes the empty space on the right).
        // The language box and PayPal button, anchored right, then align with the last bottom button.
        // Also prevents shrinking until the buttons would wrap to a second line.
        private void FitWindowToButtons()
        {
            if (_btnPanel == null || !IsHandleCreated) return;
            int need = _btnPanel.Padding.Horizontal;
            foreach (Control b in _btnPanel.Controls) need += b.PreferredSize.Width + b.Margin.Horizontal;
            need += 6;
            var wa = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Math.Max(need, 640), wa.Width);
            MinimumSize = new Size(640, MinimumSize.Height);
            ClientSize = new Size(w, ClientSize.Height);
            MinimumSize = new Size(w, MinimumSize.Height);
            FitLastColumn();   // re-fit the last column to the new width (prevents the phantom bar)
        }

        // The last column fills the remaining space (otherwise the OS paints a white sliver on the right).
        private void FitLastColumn()
        {
            if (_list == null || _list.Columns.Count < 1) return;
            // -2 = the LAST column fills EXACTLY the remaining space. The total column width can
            // therefore never exceed the client width: no phantom horizontal scrollbar, and no
            // light sliver on the right.
            _list.Columns[_list.Columns.Count - 1].Width = -2;
        }

        private void ListDrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            using (var f = new Font("Segoe UI", 10f, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, e.Header.Text, f, e.Bounds, Theme.SubText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.LeftAndRightPadding);
        }

        private void TitleDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);   // WM_NCLBUTTONDOWN / HTCAPTION
        }

        // Lays out the top banner: logo on the left (vertically centered), close/minimize at the
        // top right corner (standard spacing), language + PayPal on a line just below.
        private void LayoutTopBar()
        {
            if (_topBar == null) return;
            int w = _topBar.ClientSize.Width, h = _topBar.ClientSize.Height;

            // Close + minimize, top right corner, flush like a standard window.
            if (_btnClose != null && _btnMin != null)
            {
                _btnClose.Top = 0; _btnClose.Left = w - _btnClose.Width;
                _btnMin.Top = 0; _btnMin.Left = _btnClose.Left - _btnMin.Width;
            }

            // Logo vertically centered, on the left.
            if (_logoBox != null) { _logoBox.Left = 18; _logoBox.Top = Math.Max(8, (h - _logoBox.Height) / 2); }

            // Language + PayPal, right-aligned, below the window buttons.
            int right = w - 14;
            if (_donate != null) { _donate.Top = h - _donate.Height - 12; _donate.Left = right - _donate.Width; }
            if (_langBox != null)
            {
                _langBox.Top = (_donate != null ? _donate.Top + (_donate.Height - _langBox.Height) / 2 : h - 30);
                _langBox.Left = (_donate != null ? _donate.Left : right) - _langBox.Width - 14;
            }
            if (_langLabel != null && _langBox != null)
            {
                _langLabel.Top = _langBox.Top + (_langBox.Height - _langLabel.Height) / 2;
                _langLabel.Left = _langBox.Left - _langLabel.Width - 6;
            }
        }

        private void ToggleMax()
        {
            if (WindowState == FormWindowState.Normal)
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            else WindowState = FormWindowState.Normal;
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // Resizing from the edges (borderless window).
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                int x = unchecked((short)(long)m.LParam);
                int y = unchecked((short)((long)m.LParam >> 16));
                Point p = PointToClient(new Point(x, y));
                int g = 6;
                bool l = p.X <= g, r = p.X >= ClientSize.Width - g, t = p.Y <= g, b = p.Y >= ClientSize.Height - g;
                if (t && l) m.Result = (IntPtr)13;
                else if (t && r) m.Result = (IntPtr)14;
                else if (b && l) m.Result = (IntPtr)16;
                else if (b && r) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10;
                else if (r) m.Result = (IntPtr)11;
                else if (t) m.Result = (IntPtr)12;
                else if (b) m.Result = (IntPtr)15;
                return;
            }
            base.WndProc(ref m);
        }

        // Applies all translated strings to the fixed controls.
        private void ApplyTexts()
        {
            Text = L.T("title");
            BuildGuide();
            _langLabel.Text = L.T("langLabel");
            UpdateToggleLabel();
            _btnToggleWheel.Text = L.T("btnToggleWheel");
            _btnRefresh.Text = L.T("btnRefresh");
            _btnJoy.Text = L.T("btnJoy");
            _btnHelp.Text = L.T("btnHelp");
            _btnUp.Text = L.T("btnUp");
            _btnDown.Text = L.T("btnDown");
            // Tooltips: each button explains what it does, in the current language.
            if (_tips != null)
            {
                _tips.SetToolTip(_btnToggle, L.T("ttToggle"));
                _tips.SetToolTip(_btnUp, L.T("ttOrder"));
                _tips.SetToolTip(_btnDown, L.T("ttOrder"));
                _tips.SetToolTip(_btnToggleWheel, L.T("ttToggleWheel"));
                _tips.SetToolTip(_btnRefresh, L.T("ttRefresh"));
                _tips.SetToolTip(_btnJoy, L.T("ttJoy"));
                _tips.SetToolTip(_btnHelp, L.T("ttHelp"));
                _tips.SetToolTip(_donate, L.T("ttDonate"));
            }
            if (_donate != null) { _donate.Caption = L.T("coffee"); _donate.Recalc(); _donate.Invalidate(); }
            LayoutTopBar();
            _list.Columns[0].Text = L.T("colName");
            _list.Columns[1].Text = L.T("colVidPid");
            _list.Columns[2].Text = L.T("colState");
            if (_status != null && string.IsNullOrEmpty(_status.Text)) _status.Text = L.T("statusReady");
        }

        private void SetStatus(string msg, bool error = false)
        {
            _status.Text = msg;
            _status.ForeColor = error ? Color.FromArgb(255, 110, 110) : Theme.Text;
            _status.Refresh();
        }

        private void Refresh_()
        {
            SetStatus(L.T("statusDetecting"));
            UseWaitCursor = true;
            try
            {
                _controllers = Pnp.Detect();

                // First launch: seed the main-device list from the suggestion (known brands).
                if (!Config.Existed)
                {
                    foreach (var c in _controllers) if (c.IsWheel) Config.Wheels.Add(c.VidPid);
                    Config.Save();
                }
                // The effective "main" status comes from the saved user choice.
                foreach (var c in _controllers) c.IsWheel = Config.Wheels.Contains(c.VidPid);

                ApplySavedOrder();
                RenderList();
                RecomputeIsolatedState();
                int n = _controllers.Count;
                if (n == 0) { SetStatus(L.T("statusNone")); return; }
                // Guidance: if no main device is set, prompt to set one first.
                bool hasWheel = false;
                foreach (var c in _controllers) if (c.IsWheel) { hasWheel = true; break; }
                SetStatus(hasWheel ? string.Format(L.T("statusDetected"), n) : L.T("guideMarkWheel"), !hasWheel);
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(L.T("detectError"), ex.Message), true);
            }
            finally { UseWaitCursor = false; }
        }

        private void RenderList()
        {
            _suppress = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var c in _controllers)
            {
                var it = new ListViewItem(c.Name) { Tag = c, Checked = c.Active };
                it.SubItems.Add(c.VidPid);
                it.SubItems.Add(StateText(c));
                ApplyRowStyle(it, c);
                _list.Items.Add(it);
            }
            _list.EndUpdate();
            _suppress = false;
            FitLastColumn();
        }

        private void ApplyRowStyle(ListViewItem it, Controller c)
        {
            var baseName = c.Name;
            if (c.IsWheel)
            {
                it.Text = baseName + L.T("keepTag");
                it.BackColor = Theme.WheelRow;          // blue highlight for the main device
                it.Font = new Font(_list.Font, FontStyle.Bold);
                it.ForeColor = Theme.Text;
            }
            else
            {
                it.Text = baseName;
                it.BackColor = Theme.Bg;
                it.Font = _list.Font;
                it.ForeColor = c.Active ? Theme.Text : Theme.HiddenText;
            }
            it.SubItems[2].Text = StateText(c);
        }

        private static string StateText(Controller c) { return c.Active ? L.T("stateActive") : L.T("stateHidden"); }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppress) return;
            var item = _list.Items[e.Index];
            var c = item.Tag as Controller;
            if (c == null) return;
            bool wantActive = (e.NewValue == CheckState.Checked);

            if (c.IsWheel && !wantActive)
            {
                MessageBox.Show(this, L.T("msgWheelBody"), L.T("msgWheelTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.NewValue = CheckState.Checked;
                return;
            }

            try
            {
                if (wantActive) ShowOne(c); else HideOne(c);
                ApplyRowStyle(item, c);
                SetStatus(string.Format(L.T(wantActive ? "statusShown" : "statusHiddenOne"), c.Name));
            }
            catch
            {
                // Real failure (e.g. device held open by its own software). Clean message,
                // without brand name or raw error (which used to show garbled characters).
                e.NewValue = wantActive ? CheckState.Unchecked : CheckState.Checked;
                MessageBox.Show(this, string.Format(L.T("msgRefusedBody"), c.Name),
                    L.T("msgRefusedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus(c.Name + "." + L.T("failHint"), true);
            }
            RecomputeIsolatedState();
        }

        private void HideOne(Controller c) { Pnp.Disable(c.InstanceId); c.Active = false; _weDisabled.Add(c.InstanceId); }
        private void ShowOne(Controller c) { Pnp.Enable(c.InstanceId); c.Active = true; _weDisabled.Remove(c.InstanceId); }

        // The user marks (or unmarks) the main device(s) among the selection.
        // A device marked as main is moved to the TOP of the list; a second main goes just below it,
        // and so on (mains kept in the order they were marked). Others stay below.
        private void ToggleWheelSelection()
        {
            if (_list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, L.T("msgSelBody"), L.T("msgSelTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var touched = new List<Controller>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                var c = item.Tag as Controller;
                if (c == null) continue;
                if (Config.Wheels.Contains(c.VidPid)) { Config.Wheels.Remove(c.VidPid); c.IsWheel = false; }
                else { Config.Wheels.Add(c.VidPid); c.IsWheel = true; }
                touched.Add(c);
            }
            // Stable partition: mains first (keeping their relative order = marking order), then the rest.
            var ordered = new List<Controller>();
            foreach (var c in _controllers) if (c.IsWheel) ordered.Add(c);
            foreach (var c in _controllers) if (!c.IsWheel) ordered.Add(c);
            _controllers = ordered;
            SaveOrder();
            RenderList();
            // Keep the just-toggled device(s) selected and visible.
            foreach (ListViewItem it in _list.Items)
                if (touched.Contains(it.Tag as Controller)) { it.Selected = true; it.Focused = true; it.EnsureVisible(); }
            _list.Select();
            RecomputeIsolatedState();
        }

        // Reorders _controllers by the saved order (unknown ones stay at the end).
        private void ApplySavedOrder()
        {
            if (Config.Order.Count == 0) return;
            var ordered = new List<Controller>();
            foreach (var vp in Config.Order)
            {
                foreach (var c in _controllers)
                    if (c.VidPid == vp && !ordered.Contains(c)) { ordered.Add(c); break; }
            }
            foreach (var c in _controllers) if (!ordered.Contains(c)) ordered.Add(c);
            _controllers = ordered;
        }

        private void SaveOrder()
        {
            Config.Order.Clear();
            foreach (var c in _controllers) Config.Order.Add(c.VidPid);
            Config.Save();
        }

        // Moves the selected controller up (-1) or down (+1) = re-enable order.
        private void MoveSelected(int delta)
        {
            if (_list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, L.T("msgSelBody"), L.T("msgSelTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var c = _list.SelectedItems[0].Tag as Controller;
            if (c == null) return;
            int idx = _controllers.IndexOf(c);
            int dst = idx + delta;
            if (dst < 0 || dst >= _controllers.Count) return;
            _controllers.RemoveAt(idx);
            _controllers.Insert(dst, c);
            SaveOrder();
            RenderList();
            foreach (ListViewItem it in _list.Items)
                if (it.Tag == c) { it.Selected = true; it.Focused = true; it.EnsureVisible(); break; }
            _list.Select();
        }

        private void IsolateWheel()
        {
            bool hasWheel = false;
            foreach (var c in _controllers) if (c.IsWheel) { hasWheel = true; break; }
            if (!hasWheel)
            {
                MessageBox.Show(this, L.T("msgNoWheelBody"), L.T("msgNoWheelTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // Disable in REVERSE order (bottom to top), one by one, with a pause: the last
            // plugged device is removed first, like a real unplug (LIFO stack). The Main is never touched.
            int hidden = 0, failed = 0;
            var failedNames = new List<string>();
            UseWaitCursor = true;
            _suppress = true;
            for (int i = _list.Items.Count - 1; i >= 0; i--)
            {
                var item = _list.Items[i];
                var c = item.Tag as Controller;
                if (c == null || c.IsWheel || !c.Active) continue;
                try
                {
                    HideOne(c);
                    item.Checked = false;
                    ApplyRowStyle(item, c);
                    hidden++;
                    _list.Refresh();
                    Thread.Sleep(500);
                }
                catch { failed++; failedNames.Add(c.Name); }
            }
            _suppress = false;
            UseWaitCursor = false;
            string msg = string.Format(L.T("statusIsolated"), hidden);
            if (failed > 0) msg += string.Format(L.T("failNamed"), string.Join(", ", failedNames.ToArray())) + L.T("failHint");
            SetStatus(msg, failed > 0);
            // Also surface failures in a dialog so the user cannot miss them.
            if (failed > 0)
                MessageBox.Show(this, string.Format(L.T("msgRefusedBody"), string.Join(", ", failedNames.ToArray())),
                    L.T("msgRefusedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Re-enables in LIST order (top to bottom), one by one, with a short pause so Windows
        // registers each device before the next one.
        private void RestoreAll()
        {
            int shown = 0, failed = 0;
            UseWaitCursor = true;
            _suppress = true;
            foreach (ListViewItem item in _list.Items)
            {
                var c = item.Tag as Controller;
                if (c == null || c.Active) continue;
                try
                {
                    ShowOne(c);
                    item.Checked = true;
                    ApplyRowStyle(item, c);
                    shown++;
                    _list.Refresh();
                    Thread.Sleep(500);   // respects the enumeration order
                }
                catch { failed++; }
            }
            _suppress = false;
            UseWaitCursor = false;
            string msg = string.Format(L.T("statusRestored"), shown);
            if (failed > 0) msg += string.Format(L.T("failSuffix"), failed);
            SetStatus(msg, failed > 0);
        }

        // Smart single button: hides all except the main device, then toggles to "Restore all",
        // and back. Requires a main device to be set (otherwise a guidance dialog).
        private void OnTogglePower()
        {
            bool hasWheel = false;
            foreach (var c in _controllers) if (c.IsWheel) { hasWheel = true; break; }
            if (!hasWheel)
            {
                MessageBox.Show(this, string.Format(L.T("msgDefineBody"), L.T("btnToggleWheel")),
                    L.T("msgDefineTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_isolated) IsolateWheel(); else RestoreAll();
            RecomputeIsolatedState();
        }

        // Computes the real state from the devices: isolated = at least one "non main" device is
        // hidden (so there is something to restore), even if some failed to disable. If nothing is
        // hidden (e.g. all failed), the button stays "Hide all". Updates the button label.
        private void RecomputeIsolatedState()
        {
            int hiddenOthers = 0;
            foreach (var c in _controllers)
            {
                if (c.IsWheel) continue;
                if (!c.Active) hiddenOthers++;
            }
            _isolated = (hiddenOthers > 0);
            UpdateToggleLabel();
        }

        // Normal: "Hide all except the Main". Isolated: "Restore all".
        private void UpdateToggleLabel()
        {
            if (_btnToggle == null) return;
            _btnToggle.Text = _isolated ? L.T("btnRestore") : L.T("btnIsolate");
            _btnToggle.BackColor = _isolated ? Theme.AccentDark : Theme.Accent;
            _btnToggle.FlatAppearance.MouseOverBackColor = _isolated ? Theme.Accent : Theme.AccentHover;
            _btnToggle.UseVisualStyleBackColor = false;
        }

        // Opens the Windows "Game Controllers" window to check the effect live.
        private void OpenJoyCpl()
        {
            try { Process.Start(new ProcessStartInfo("joy.cpl") { UseShellExecute = true }); }
            catch { try { Process.Start("control", "joy.cpl"); } catch { } }
        }

        // Dark help window (matching the app) able to display colored text.
        // The body may contain <r>...</r> segments rendered in RED (important notes).
        private void ShowHelp()
        {
            using (var dlg = new Form())
            {
                dlg.Text = L.T("helpTitle");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.Sizable;
                dlg.MinimizeBox = false; dlg.MaximizeBox = false; dlg.ShowInTaskbar = false;
                dlg.BackColor = Theme.Bg; dlg.ForeColor = Theme.Text;
                dlg.ClientSize = new Size(740, 580);
                dlg.MinimumSize = new Size(540, 380);
                dlg.ShowIcon = false;   // no icon in the help window title bar

                var rtb = new RichTextBox
                {
                    Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
                    BackColor = Theme.Bg, ForeColor = Theme.SubText, TabStop = false,
                    Font = new Font("Segoe UI", 10f), ScrollBars = RichTextBoxScrollBars.Vertical
                };
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 12, 8), BackColor = Theme.Bg };
                pad.Controls.Add(rtb);

                var btnOk = new Button
                {
                    Text = "OK", Dock = DockStyle.Bottom, Height = 42, FlatStyle = FlatStyle.Flat,
                    BackColor = Theme.Accent, ForeColor = Color.White, DialogResult = DialogResult.OK,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
                };
                btnOk.FlatAppearance.BorderSize = 0;
                btnOk.FlatAppearance.MouseOverBackColor = Theme.AccentHover;

                dlg.Controls.Add(pad);
                dlg.Controls.Add(btnOk);
                dlg.AcceptButton = btnOk;
                dlg.Shown += (s, e) =>
                {
                    TryDarkTitleBar(dlg.Handle);
                    AppendFormattedHelp(rtb, L.T("helpBody"));
                    rtb.SelectionStart = 0; rtb.SelectionLength = 0; rtb.ScrollToCaret();
                };
                dlg.ShowDialog(this);
            }
        }

        // Fills the RichTextBox in a very readable way:
        //   intro and step titles "STEP N. ... :" in BOLD,
        //   each step description on a NEW line, slightly indented,
        //   <r>...</r> note in RED, keyword "IMPORTANT:" in bold.
        private static void AppendFormattedHelp(RichTextBox rtb, string body)
        {
            Color gray = Theme.SubText, white = Theme.Text, red = Color.FromArgb(255, 95, 95);
            var stepRx = new Regex(@"^\p{Lu}{2,}\s+\d+\.");
            var kwRx = new Regex(@"^\p{Lu}{2,}\s*:");
            string[] paras = body.Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.None);
            bool shown = false;
            foreach (var raw in paras)
            {
                string para = raw.Trim();
                if (para.Length == 0) continue;
                bool first = !shown;
                if (shown) AppendRun(rtb, "\n\n", gray, false);
                shown = true;

                if (para.StartsWith("<r>"))
                {
                    AppendRun(rtb, para.Replace("<r>", "").Replace("</r>", "").Trim(), red, true);
                }
                else if (stepRx.IsMatch(para))   // "STEP N. Title: description"
                {
                    int c = para.IndexOf(": ", StringComparison.Ordinal);
                    string header = c >= 0 ? para.Substring(0, c + 1) : para;
                    string desc = c >= 0 ? para.Substring(c + 2).Trim() : "";
                    AppendRun(rtb, header, white, true);
                    if (desc.Length > 0) AppendRun(rtb, "\n    " + desc, gray, false);
                }
                else if (kwRx.IsMatch(para))      // "IMPORTANT: ..." (keyword in bold)
                {
                    int c = para.IndexOf(':');
                    AppendRun(rtb, para.Substring(0, c + 1), white, true);
                    AppendRun(rtb, para.Substring(c + 1), gray, false);
                }
                else                               // intro (first line) bold, otherwise gray
                {
                    AppendRun(rtb, para, first ? white : gray, first);
                }
            }
        }

        private static void AppendRun(RichTextBox rtb, string text, Color color, bool bold)
        {
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor = color;
            rtb.SelectionFont = new Font("Segoe UI", 10f, bold ? FontStyle.Bold : FontStyle.Regular);
            rtb.AppendText(text);
        }

        // Dark title bar (Windows 10 2004+/11). No effect on older versions.
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        private static void TryDarkTitleBar(IntPtr hwnd)
        {
            try { int on = 1; if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4); } catch { }
        }

        // On the very first launch: ask for the language BEFORE showing the help,
        // so the guide is shown in the chosen language.
        private void ChooseLanguageFirstRun()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "Language / Langue";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false; dlg.MinimizeBox = false; dlg.ShowInTaskbar = false;
                dlg.ClientSize = new Size(330, 130);
                var lbl = new Label { Text = "Choose your language / Choisissez votre langue :", AutoSize = true, Left = 12, Top = 16 };
                var cb = new ComboBox { Left = 12, Top = 46, Width = 306, DropDownStyle = ComboBoxStyle.DropDownList };
                cb.Items.AddRange(L.Names);
                cb.SelectedIndex = L.Lang;
                var ok = new Button { Text = "OK", Left = 243, Top = 88, Width = 75, DialogResult = DialogResult.OK };
                dlg.Controls.Add(lbl); dlg.Controls.Add(cb); dlg.Controls.Add(ok);
                dlg.AcceptButton = ok;
                dlg.ShowDialog(this);
                L.Lang = cb.SelectedIndex >= 0 ? cb.SelectedIndex : 0;
            }
            Config.Save();
            ApplyTexts();
            RenderList();
            FitWindowToButtons();
            _suppress = true; _langBox.SelectedIndex = L.Lang; _suppress = false;
        }

        private void OnClosingSafetyNet(object sender, FormClosingEventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (_weDisabled.Count == 0) return;
            foreach (var id in new List<string>(_weDisabled))
            {
                try { Pnp.Enable(id); } catch { }
            }
        }

        [STAThread]
        public static void Main()
        {
            Config.Load();
            // Language: config if present, otherwise system culture, otherwise English.
            if (Config.LangIndex >= 0) L.Lang = Config.LangIndex;
            else
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
                for (int i = 0; i < L.Codes.Length; i++) if (L.Codes[i] == two) L.Lang = i;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
