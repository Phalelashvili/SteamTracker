#!/usr/bin/env python3
import os
import time
import psycopg2
import threading
import traceback
import elasticsearch
import multiprocessing
from argparse import ArgumentParser


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

        if self.count > self.end:
            raise StopIteration()
        return avatar

    def get_avatar(self):
        line = ""
        while line == "":
            self.count += 1
            line = self.file.readline()[:-1]  # remove \n

        return line


def remove_image(es, avatar):
    try:
        es.delete(index="images", doc_type="image", id=avatar)
    except Exception as e:
        print(f"{avatar} - {e}")


def in_database(cursor, avatar):
    cursor.execute(f"SELECT * FROM users WHERE avatar = '{avatar}'")
    return cursor.fetchone() is not None


def new_process(filename, thread_range, offset, progress):
    global removed
    removed = 0
    es = elasticsearch.Elasticsearch(timeout=60)
    connection = psycopg2.connect(database="steamtracker")
    connection.autocommit = True

    cursor = connection.cursor()

    avatars = AvatarIterator(args.fileName, thread_range,
                             offset=offset, progress=progress)
    print(f"started process {offset} -> {avatars.end}")

    for _ in range(args.threadCount):
        threading.Thread(target=new_thread, args=(avatars,)).start()

    while True:
        if offset == 0: os.system('clear')
        print(f"offset {avatars.offset} | {removed}/{thread_range}")
        time.sleep(1)


def new_thread(avatars):
    global removed

    es = elasticsearch.Elasticsearch(timeout=60)
    connection = psycopg2.connect(database="steamtracker")
    connection.autocommit = True

    cursor = connection.cursor()

    for avatar in avatars:
        if not in_database(cursor, avatar):
            removed += 1
            remove_image(es, avatar)


if __name__ == '__main__':
    parser = ArgumentParser()

    parser.add_argument('fileName', help='''
        /etc/logstash-7.5.2/bin/logstash -f output-csv.conf
        file created by logstash dump
        https://medium.com/@shaonshaonty/export-data-from-elasticsearch-to-csv-caaef3a19b69
        note: add `docinfo => true` to config
    ''')
    parser.add_argument('lineCount', type=int)

    parser.add_argument('-p', '--processCount', type=int, default=1)
    parser.add_argument('-t', '--threadCount', type=int, default=1)
    parser.add_argument('-pr', '--progress', type=int, default=0)

    args = parser.parse_args()

    thread_range = args.lineCount // args.processCount

    offset = 0
    for i in range(args.processCount):
        multiprocessing.Process(
            target=new_process,
            args=(args.fileName, thread_range, offset, args.progress,)
        ).start()
        offset += thread_range
