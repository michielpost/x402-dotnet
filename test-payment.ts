import { Keypair, PublicKey, Connection } from "@solana/web3.js";
import { createPaymentHandler } from "@faremeter/payment-solana-exact";
import { wrap } from "@faremeter/fetch";
import { solana } from "@faremeter/info";
import * as fs from "fs";

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const keypairData = JSON.parse(fs.readFileSync("./payer-wallet.json", "utf-8"));
const keypair = Keypair.fromSecretKey(Uint8Array.from(keypairData));

const network = "solana-mainnet-beta";
const connection = new Connection("https://api.mainnet-beta.solana.com");
const usdcInfo = solana.lookupKnownSPLToken("mainnet-beta", "USDC");
const usdcMint = new PublicKey(usdcInfo.address);

const wallet = {
  network,
  publicKey: keypair.publicKey,
  updateTransaction: async (tx) => {
    tx.sign([keypair]);
    return tx;
  },
};

const handler = createPaymentHandler(wallet, usdcMint, connection);
const fetchWithPayer = wrap(fetch, { handlers: [handler] });

const targetUrl = process.argv[2] || "https://localhost:7154/resource/protected";

const response = await fetchWithPayer(targetUrl, {
  method: "GET",
  headers: { "Accept": "application/json" },
});

console.log("Status:", response.status);

if (response.ok) {
  const data = await response.json();
  console.log("Success:", JSON.stringify(data, null, 2));
} else {
  const text = await response.text();
  console.error("Failed:", response.status, text);
}
