// Synthetic loopback acceptance sink. Stores evidence only; never relays mail.
const net = require('node:net'), fs = require('node:fs'), path = require('node:path');
const port = Number(process.argv[2]), root = path.resolve(process.argv[3]);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw Error('Explicit loopback port required');
fs.mkdirSync(root, { recursive: true });
let serial = 0;
net.createServer(socket => {
    socket.setTimeout(30000, () => socket.destroy());
    let buffer = '', data = false, lines = [], sender = '', recipients = [], received = 0;
    socket.on('error', () => {});
    socket.write('220 local.invalid synthetic sink\r\n');
    socket.on('data', chunk => {
        buffer += chunk.toString('utf8');
        received += chunk.length;
        if (received > 4200000) return socket.destroy();
        let boundary;
        while ((boundary = buffer.indexOf('\r\n')) >= 0) {
            const line = buffer.slice(0, boundary); buffer = buffer.slice(boundary + 2);
            if (data) {
                if (line !== '.') { lines.push(line.startsWith('..') ? line.slice(1) : line); continue; }
                data = false;
                const id = `${Date.now()}-${++serial}`;
                fs.writeFileSync(path.join(root, id + '.eml'), lines.join('\r\n') + '\r\n');
                fs.writeFileSync(path.join(root, id + '.json'), JSON.stringify({ sender, recipients, at: new Date().toISOString() }));
                lines = [];
                const mode = fs.existsSync(path.join(root, 'mode.txt')) ? fs.readFileSync(path.join(root, 'mode.txt'), 'utf8').trim() : 'Accepted';
                if (mode === 'Unknown') return socket.destroy();
                if (mode === 'Hold') continue;
                socket.write('250 synthetic DATA accepted\r\n');
            } else if (/^(EHLO|HELO)/i.test(line)) socket.write('250 local.invalid\r\n');
            else if (/^MAIL FROM:/i.test(line)) { sender = line.slice(10); recipients = []; socket.write('250 sender accepted\r\n'); }
            else if (/^RCPT TO:/i.test(line)) { recipients.push(line.slice(8)); socket.write('250 recipient accepted\r\n'); }
            else if (/^DATA$/i.test(line)) { data = true; socket.write('354 end with dot\r\n'); }
            else if (/^QUIT$/i.test(line)) socket.end('221 done\r\n');
            else socket.write('250 ok\r\n');
        }
    });
}).listen(port, '127.0.0.1', () => console.log('Synthetic SMTP sink listening on loopback port ' + port));
