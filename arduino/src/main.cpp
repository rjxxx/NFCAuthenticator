#include <Arduino.h>
#include "HID-Project.h"
#include <Wire.h>
#include <SPI.h>
#include <Adafruit_PN532.h>


#define PN532_SCK  (15)
#define PN532_MOSI (16)
#define PN532_SS   (10)
#define PN532_MISO (14)

Adafruit_PN532 nfc(PN532_SCK, PN532_MISO, PN532_MOSI, PN532_SS);


void setup() {
    Serial.begin(115200);
    while (!Serial) {}

    nfc.begin();
    uint32_t versiondata = nfc.getFirmwareVersion();
    if (!versiondata) {
        Serial.print("Didn't find PN53x board");
        while (1); // halt
    }


    // Got ok data, print it out!
    Serial.print("Found chip PN5");
    Serial.println((versiondata >> 24) & 0xFF, HEX);
    Serial.print("Firmware ver. ");
    Serial.print((versiondata >> 16) & 0xFF, DEC);
    Serial.print('.');
    Serial.println((versiondata >> 8) & 0xFF, DEC);

    nfc.setPassiveActivationRetries(0xFF);

    // configure board to read RFID tags
    nfc.SAMConfig();

}

bool readCartUid(uint8_t *uid) {
    bool success;

    uint8_t responseLength = 128;
    uint8_t response[128];

    success = nfc.inListPassiveTarget();
    if (!success) {
        Serial.println("1");
        return false;
    }
    Serial.println("Found something!");
    uint8_t selectPpse[] = {
            0x00,
            0xA4,
            0x04,
            0x00,
            0x0E,
            0x32, 0x50, 0x41, 0x59, 0x2E, 0x53, 0x59, 0x53, 0x2E, 0x44, 0x44, 0x46, 0x30, 0x31,
            0x00
    };

    success = nfc.inDataExchange(selectPpse, sizeof(selectPpse), response, &responseLength);
    if (!success) {
        Serial.println("2");
        return false;
    }
    Serial.print("responseLength: ");
    Serial.println(responseLength);
    Adafruit_PN532::PrintHexChar(response, responseLength);
    uint8_t selectAid[] = {0x00,                                     /* CLA */
                           0xA4,                                     /* INS */
                           0x04,                                     /* P1  */
                           0x00,                                     /* P2  */
                           0x07,                                     /* Length of AID  */
                           0xA0, 0x00, 0x00, 0x06, 0x58, 0x10, 0x10, /* AID defined on Android App */
                           0x00 /* Le  */};
    uint8_t back[128];
    uint8_t length = 128;
    success = nfc.inDataExchange(selectAid, sizeof(selectAid), back, &length);
    if (success) {
        Serial.print("responseLength: ");
        Serial.println(length);
        Adafruit_PN532::PrintHexChar(back, length);
        memcpy(uid, back, sizeof(back[0]) * length);
        return true;
    } else {
        Serial.println("3");
    }

    return false;
}

uint8_t uid[128];

void loop() {
    if (readCartUid(uid)) {
//        Adafruit_PN532::PrintHexChar(uid, 128);
        delay(5000);
        Serial.println("Done");
    }
    delay(10);
}