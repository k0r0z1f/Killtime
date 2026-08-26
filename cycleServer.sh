while(true)
do
bash start.sh &
sleep 1500s
pid=$(pidof ruby)
echo "pid is $pid"
kill -9 $pid
sleep 1s
done
