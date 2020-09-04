#!/usr/bin/env python3
import os
import time
import threading
import urllib.error
import elasticsearch
import multiprocessing
from argparse import ArgumentParser
from image_match.elasticsearch_driver import SignatureES


class AvatarIterator:
    def __init__(self, filename, end, offset=0, progress=0):
        self.file = open(filename)
        self.offset = offset
        self.count = 0
        self.end = offset + end

        for _ in range(offset + progress):
            self.get_avatar()

    def __iter__(self):
        return self

    def __next__(self):
        avatar = self.get_avatar()
        while True:
            if es.exists(index="images", id=avatar, doc_type="_doc"):
                for _ in range(args.skipStep):
                    # checking if every avatar exists takes long time, skipping saves us some time
                    # but will miss out some avatars, updater isn't meant to be frequently
                    # restarted anyway, but if it's necessary, this will catch up faster
                    avatar = self.get_avatar()
            else:
                break

        if self.count > self.end:
            raise StopIteration()
        return avatar

    def get_avatar(self):
        line = ""
        while line == "":
            self.count += 1
            line = self.file.readline()[:-1]  # remove \n

        return line


def add_image(avatar):
    try:
        ses.add_image(avatar)
    except urllib.error.HTTPError as e:
        print(f"{avatar} - {e.code}")
        if e.code == 404:
            es.index(index="images", doc_type="image", id=avatar, body={})
    except elasticsearch.exceptions.ConnectionTimeout:
        print("ConnectionTimeout")
    except Exception as e:
        print(f"{avatar} - {e}")


def new_process(thread_range, offset):
    global es, ses
    es = elasticsearch.Elasticsearch(timeout=60)
    ses = SignatureES(es)

    avatars = AvatarIterator(args.fileName, thread_range,
                             offset=offset, progress=args.progress)
    print(f"started process {offset} -> {avatars.end}")

    for _ in range(args.threadCount):
        threading.Thread(target=new_thread, args=(avatars,)).start()

    while True:
        if offset == 0:
            os.system('clear')
        msg = f"offset {avatars.offset} | {avatars.count - avatars.offset}"
        if avatars.count - avatars.offset >= thread_range:
            msg += " (finished)"
        print(msg)
        time.sleep(1)


def new_thread(avatars):
    for avatar in avatars:
        add_image(avatar)


if __name__ == '__main__':
    parser = ArgumentParser()

    # file created by dumping distinct avatars from users which aren't saved
    parser.add_argument('fileName')
    parser.add_argument('lineCount', type=int)

    parser.add_argument('-p', '--processCount', type=int, default=1)
    parser.add_argument('-t', '--threadCount', type=int, default=1)
    parser.add_argument('-pr', '--progress', type=int, default=0)
    parser.add_argument('-s', '--skipStep', type=int, default=1)

    args = parser.parse_args()

    thread_range = args.lineCount // args.processCount

    offset = 0
    for i in range(args.processCount):
        multiprocessing.Process(
            target=new_process,
            args=(thread_range, offset)
        ).start()
        offset += thread_range
