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
uint8_t rawhidData[255];
uint8_t selectPpse[] = {
        0x00,
        0xA4,
        0x04,
        0x00,
        0x0E,
        0x32, 0x50, 0x41, 0x59, 0x2E, 0x53, 0x59, 0x53, 0x2E, 0x44, 0x44, 0x46, 0x30, 0x31,
        0x00
};


void setup() {
    RawHID.begin(rawhidData, sizeof(rawhidData));

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

bool withRetry(uint8_t *send, uint8_t sendLength,
               uint8_t *response, uint8_t *responseLength) {
    bool success;
    int count = 0;
    while (count < 5) {
        success = nfc.inDataExchange(send, sendLength, response, responseLength);
        if (success) {
            return true;
        }
        count++;
    }
    return false;
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

    success = withRetry(selectPpse, sizeof(selectPpse), response, &responseLength);
    if (!success) {
        Serial.println("2");
        return false;
    }
    Serial.print("responseLength1: ");
    Serial.println(responseLength);
    Adafruit_PN532::PrintHexChar(response, responseLength);

    uint8_t i = 0;
    uint8_t size = 0;
    uint8_t *selectAid = nullptr;
    uint8_t j = 5;
    uint8_t k = 0;
    while (i < sizeof(response)) {
        if (size == 0) {
            if (response[i] == 0x4F) {
                size = response[i + 1];
                selectAid = new uint8_t[6 + size];
                selectAid[0] = 0x00;
                selectAid[1] = 0xA4;
                selectAid[2] = 0x04;
                selectAid[3] = 0x00;
                selectAid[4] = size;
                i++;
                continue;
            }
            i++;
        } else {
            selectAid[j++] = response[++i];
            if (++k == size) {
                selectAid[++j] = 0x00;
                break;
            }
        }
    }

    if (selectAid == nullptr) {
        Serial.println("nullptr");
        return false;
    }

    Adafruit_PN532::PrintHexChar(selectAid, size + 6);

    uint8_t back[128];
    uint8_t length = 128;
    success = withRetry(selectAid, size + 6, back, &length);
    if (success) {
        Serial.print("responseLength2: ");
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
        RawHID.write(uid, sizeof(uid));
//        Adafruit_PN532::PrintHexChar(uid, 128);
//        delay(5000);
        Serial.println("Done");
    }
    delay(10);
}